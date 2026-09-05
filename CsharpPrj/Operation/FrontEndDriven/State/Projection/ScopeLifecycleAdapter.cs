using System.Text.Json;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Eventing;
using FrontEndDriven.Gate;
using FrontEndDriven.Lifecycle;
using FrontEndDriven.Validation;
using Share.StateManagement;
using TConfig;
using TConfig.Abstractions;
using ScopeLoadResult = Share.StateManagement.LoadResult;

namespace FrontEndDriven.State.Projection;

/// <summary>
/// 作用域生命周期 Provider：组合 ScopeLifecycleStateMachine（状态转移，Operation 层纯逻辑）
/// + FrontEndDriven 渲染段（原 EcsGate.DoLoadAndRender 收敛于此）。
/// LoadAsync 协议：幂等短路（R6）→ Loading → 渲染委托（宿主解析→配置加载→UI 线程 EcsGate 原子渲染）
/// → HostActualChildCount&gt;0 置 MarkMounted（R5 唯一写入点）→ Loaded/Failed。
/// </summary>
public sealed class ScopeLifecycleAdapter : IScopeLifecycle
{
    private readonly ScopeLifecycleStateMachine _machine;
    private readonly EcsGate _gate;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly Func<string, Task<UIConfiguration?>> _configLoader;
    private readonly IContainerLifecycle _containers;
    private readonly ICompositionLifecycle _composition;
    private readonly HostMountService _hostMount;
    private readonly EventPlatform? _platform;

    public ScopeLifecycleAdapter(
        EcsGate gate,
        IUIDispatcher uiDispatcher,
        Func<string, Task<UIConfiguration?>> configLoader,
        ICompositionLifecycle composition,
        IContainerLifecycle containers,
        HostMountService hostMount,
        EventPlatform? platform = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _hostMount = hostMount ?? throw new ArgumentNullException(nameof(hostMount));
        _platform = platform;

        _machine = new ScopeLifecycleStateMachine(
            renderDelegate: RenderAsync,
            destroyDelegate: DestroyScope);
    }

    /// <summary>
    /// M6·销毁单路径事件发布（方案 §四 R5 M6）：状态机销毁路径与 EcsGate.DestroyScopeAsync 语义等价——
    /// 发布 Destroying → ScopeDestroyed（composition 销毁实体，StateCenter 单一路径，绝不 Dispose）→ 发布 Destroyed。
    /// 销毁事件唯一来源 = 状态机销毁路径（重载 scope 时状态机记录 Destroying→Destroyed 过渡，事件发布一并完成）。
    /// </summary>
    private void DestroyScope(string scope)
    {
        var platform = _platform;
        platform?.Publish(PlatformEvent.Destroying(scope));

        _composition.ScopeDestroyed(scope);

        platform?.Publish(PlatformEvent.Destroyed(scope));
    }

    // ── IScopeLifecycle：委托状态机 ──

    public Task<ScopeLoadResult> LoadAsync(string scope, string configPath, bool forceReload = false)
        => _machine.LoadAsync(scope, configPath, forceReload);

    public Task DestroyAsync(string scope)
        => _machine.DestroyAsync(scope);

    public ScopeLifecycleInfo? GetState(string scope)
        => _machine.GetState(scope);

    public IReadOnlyCollection<string> GetScopes()
        => _machine.GetScopes();

    public event Action<ScopeLifecycleInfo>? ScopeStateChanged
    {
        add => _machine.ScopeStateChanged += value;
        remove => _machine.ScopeStateChanged -= value;
    }

    // ── 渲染段（StateMachine 委托的 Func） ──

    private async Task<ScopeLoadResult> RenderAsync(string scope, string configPath, bool forceReload)
    {
        // ① 宿主解析（唯一权威链路：HostMountService → ContainerRegistry）
        var handle = _hostMount.ResolveByScope(scope);
        if (handle == null)
            return new ScopeLoadResult(scope, false, 0, 0, $"scope '{scope}' 未绑定宿主，无法渲染");

        var host = handle.Control;
        if (host == null)
            return new ScopeLoadResult(scope, false, 0, 0, $"宿主 '{scope}' 的控件未注册到 ControlPool，无法渲染");

        // ② 配置加载
        var config = await _configLoader(configPath).ConfigureAwait(false);
        if (config == null)
            return new ScopeLoadResult(scope, false, 0, 0, "配置加载失败");

        // ②·F1 渲染宿主循环防护（RenderHostCycleGuard）：状态管线渲染段唯一权威闸门。
        // 命中"配置声明了它正在渲染进入的作用域/宿主控件"→ 拒绝渲染，Succeeded=false + 校验文案，
        // 由 ScopeLifecycleStateMachine 落 Failed 终态，走既有错误链路，绝不进入 ⑤ UI 渲染——
        // 从源头阻断"壳误载为内容"的循环宿主注入（不再触发 WinForms 循环控件引用异常）。
        var cycleGuard = RenderHostCycleGuard.Validate(config, scope, handle);
        if (!cycleGuard.Allowed)
            return new ScopeLoadResult(scope, false, 0, 0, cycleGuard.Error);

        // ③ 生命周期：OnBeforeLoad 钩子（声明于 config.Lifecycle，经统一函数通道执行；
        //    失败不阻断渲染——钩子是附加行为，渲染主流程不受其影响）
        if (_platform != null && config.Lifecycle?.OnBeforeLoad is { Count: > 0 })
        {
            await ExecuteHooksSafelyAsync(config.Lifecycle.OnBeforeLoad, scope).ConfigureAwait(false);
        }

        // ④ 事件：Loading
        var phaseId = Path.GetFileName(configPath) ?? scope;
        if (_platform != null)
        {
            await _platform.PublishAsync(PlatformEvent.Loading(phaseId, scope, configPath)).ConfigureAwait(false);
        }

        // ⑤ UI 线程渲染（EcsGate 原子操作：内部先销毁旧 scope → composition.Execute → ExecuteRenderStage(scope)）
        var result = await _uiDispatcher.InvokeAsync(
            () => _gate.LoadConfigAndRenderAsync(config, scope, host)).ConfigureAwait(false);

        // ⑥ R5 唯一写入点 + M3 双条件：宿主真实收到 root（rootAttached）∧ 内容层控件物理核验挂载（contentMounted）。
        // 仅 root 入宿主不构成挂载成功——内容层可能物理 orphan（如配置误嵌套），必须二者同时满足才 MarkMounted。
        var rootAttached = result.HostActualChildCount > 0;
        var contentMounted = result.ContentMounted > 0;
        var mountVisible = rootAttached && contentMounted;
        if (mountVisible)
        {
            _containers.MarkMounted(scope, mounted: true);
        }

        // ⑦ 生命周期：OnAfterLoad 钩子（挂载成功后才执行；渲染失败时跳过——避免在失效 UI 上运行初始化污染状态）。
        //    向后兼容：未声明 lifecycle.onAfterLoad 时回退执行旧 initCommands（initCommands → lifecycle 迁移期双支持）。
        if (mountVisible)
        {
            var afterLoadHooks = config.Lifecycle?.OnAfterLoad;
            if (afterLoadHooks is { Count: > 0 })
            {
                await ExecuteHooksSafelyAsync(afterLoadHooks, scope).ConfigureAwait(false);
            }
            else if (config.InitCommands is { Count: > 0 })
            {
                await ExecuteInitCommandsAsync(config, scope).ConfigureAwait(false);
            }
        }

        // ⑧ 事件：Loaded
        if (_platform != null)
        {
            await _platform.PublishAsync(PlatformEvent.Loaded(
                phaseId, scope, configPath, result.RenderExecuted ? result.MountedCount : 0)).ConfigureAwait(false);
        }

        // M3 宿主/结构判据合一：result.Succeeded（含物理核验失败=OrphanCount）∧ root 入宿主 ∧ 内容层物理挂载。
        // 三者任一不满足 → succeeded=false，杜绝「root 挂上但内容层物理 orphan 仍报成功」的假绿。
        var succeeded = result.Succeeded && rootAttached && contentMounted;

        // 诊断：渲染成败判据（result.Succeeded ∧ rootAttached ∧ contentMounted）落日志，
        // 区分「渲染异常」「宿主空（未挂载）」「内容层物理未挂载（配置误嵌套）」。
        FrontEndDrivenLog.Log("ScopeLifecycleAdapter_render_outcome",
            new
            {
                scope,
                configPath,
                renderSucceeded = result.Succeeded,
                rootAttached,
                hostActualChildCount = result.HostActualChildCount,
                contentMounted,
                mountVisible,
                succeeded
            });

        return new ScopeLoadResult(
            scope,
            succeeded,
            0,
            result.HostActualChildCount,
            succeeded
                ? null
                : !rootAttached
                    ? "宿主空（HostActualChildCount=0）"
                    : !contentMounted
                        ? "内容层控件物理未挂载（ContentMounted=0，疑似配置误嵌套）"
                        : "渲染未成功");
    }

    // ── 生命周期钩子执行（安全包装：钩子失败仅记日志，不阻断加载主流程）──

    private async Task ExecuteHooksSafelyAsync(IEnumerable<string> hookNames, string scope)
    {
        if (_platform == null)
            return;

        try
        {
            await LifecycleHookExecutor.ExecuteHooksAsync(_platform, hookNames, scope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("ScopeLifecycleAdapter_lifecycle_hook_error",
                new { scope, error = ex.Message, exceptionType = ex.GetType().FullName });
        }
    }

    /// <summary>
    /// 旧 initCommands 向后兼容执行（initCommands → lifecycle.onAfterLoad 迁移期双支持）：
    /// 普通条目按函数名直接调用（旧 initCommandMode="function" 语义）；
    /// "exec X k=v..." 命令式条目经统一符号路由（见 ExecuteInitExecAsync）。
    /// </summary>
    private async Task ExecuteInitCommandsAsync(UIConfiguration config, string scope)
    {
        if (_platform == null || config.InitCommands is not { Count: > 0 })
            return;

        foreach (var raw in config.InitCommands)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            try
            {
                if (entry.StartsWith("exec ", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteInitExecAsync(entry, scope).ConfigureAwait(false);
                    continue;
                }

                await _platform.InvokeFunctionAsync(entry,
                    source: "LifecycleHook", timeoutMs: 30_000, scope: scope).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("ScopeLifecycleAdapter_init_command_error",
                    new { scope, command = entry, error = ex.Message, exceptionType = ex.GetType().FullName });
            }
        }
    }

    /// <summary>
    /// 执行 `exec &lt;符号&gt; [k=v...]` init 命令。文档化形态（SKILL.md §2.4）：
    ///   - `exec &lt;Workflow&gt; _stepSelector=&lt;Step&gt;` → 工作流路径（ExecuteWorkflow + JSON input）
    ///   - `exec &lt;Func&gt; [k=v...]` → 函数路径（统一函数通道；FunctionHub 对已注册函数自动包装为单节点工作流）
    /// `output=&lt;Control&gt;` 为输出目标指令，不作为函数参数传递。
    /// 修复：此前 exec 一律硬编码调用 "ExecuteWorkflow"，导致已注册函数（如 Cline_*）被误按工作流名解析而报"工作流未找到"。
    /// </summary>
    private async Task ExecuteInitExecAsync(string entry, string scope)
    {
        var tokens = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return;

        var symbol = tokens[1];
        var (namedParams, hasStepSelector, outputTarget) = ParseInitExecParams(tokens.Skip(2));

        // 工作流形态（显式 _stepSelector）直走 ExecuteWorkflow；其余先按函数解析，失败再回退工作流路径。
        if (hasStepSelector)
        {
            await ExecuteInitWorkflowAsync(symbol, namedParams, scope).ConfigureAwait(false);
            return;
        }

        var result = await _platform!.InvokeFunctionAsync(symbol, namedParams,
            source: "LifecycleHook", timeoutMs: 30_000, scope: scope).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            // 修复：exec <Func> output=<Control> 写回函数结果（SKILL.md §2.4「执行函数并写回控件」语义）。
            // 此前 output= 被丢弃、结果未回写控件，导致 initCommands 自动填充全部失效。
            if (!string.IsNullOrWhiteSpace(outputTarget))
                WriteInitOutput(outputTarget, result.Output);
            return;
        }

        FrontEndDrivenLog.Log("ScopeLifecycleAdapter_init_function_failed",
            new { scope, symbol, error = result.ErrorMessage });
        await ExecuteInitWorkflowAsync(symbol, namedParams, scope).ConfigureAwait(false);
    }

    /// <summary>经统一函数通道执行工作流（调用 ExecuteWorkflow 函数，input 组装为 JSON 以携带 _stepSelector）。</summary>
    private async Task ExecuteInitWorkflowAsync(string workflowName, Dictionary<string, object?> namedParams, string scope)
    {
        var input = namedParams.Count > 0 ? JsonSerializer.Serialize(namedParams) : "{}";

        var result = await _platform!.InvokeFunctionAsync("ExecuteWorkflow",
            new Dictionary<string, object?>
            {
                ["workflowName"] = workflowName,
                ["input"] = input
            },
            source: "LifecycleHook", timeoutMs: 30_000, scope: scope).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            FrontEndDrivenLog.Log("ScopeLifecycleAdapter_init_workflow_failed",
                new { scope, workflowName, error = result.ErrorMessage });
        }
    }

    /// <summary>解析 exec 参数：k=v 命名参数；`output=` 单独提取为输出目标控件；识别 _stepSelector 形态。</summary>
    private static (Dictionary<string, object?> NamedParams, bool HasStepSelector, string? OutputTarget) ParseInitExecParams(IEnumerable<string> tokens)
    {
        var namedParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hasStepSelector = false;
        string? outputTarget = null;

        foreach (var tok in tokens)
        {
            if (tok.StartsWith("output=", StringComparison.OrdinalIgnoreCase))
            {
                outputTarget = tok.Substring("output=".Length);
                continue;
            }

            var eq = tok.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = tok.Substring(0, eq);
            var value = tok.Substring(eq + 1);
            namedParams[key] = value;
            if (string.Equals(key, "_stepSelector", StringComparison.OrdinalIgnoreCase))
                hasStepSelector = true;
        }

        return (namedParams, hasStepSelector, outputTarget);
    }

    /// <summary>
    /// 将 init 命令的函数结果写回 output=&lt;Control&gt; 目标控件。
    /// 经 IEcsStateAccessor（与 set 命令/按钮写回同源），写 ECS 真源并投影物理控件，get 可读到。
    /// 仅处理字符串结果；列表型结果（如 ListData）不支持经 SetValue 写回，跳过并记日志。
    /// </summary>
    private static void WriteInitOutput(string controlName, object? output)
    {
        var accessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
        if (accessor == null)
        {
            FrontEndDrivenLog.Log("ScopeLifecycleAdapter_init_output_no_accessor", new { controlName });
            return;
        }

        if (output is not string text)
        {
            FrontEndDrivenLog.Log("ScopeLifecycleAdapter_init_output_skipped",
                new { controlName, type = output?.GetType().FullName });
            return;
        }

        accessor.SetValue(controlName, text);
    }
}
