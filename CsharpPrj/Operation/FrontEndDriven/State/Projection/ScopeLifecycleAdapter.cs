using System.Text.Json;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Eventing;
using FrontEndDriven.Gate;
using FrontEndDriven.Lifecycle;
using FrontEndDriven.Pipeline;
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

        // 销毁出口上收单一实现（与 EcsGate / RenderTransaction 同一路径）。
        CompositionTeardown.DestroyScope(_composition, scope);

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
            return Reject(scope, configPath, "host-binding", $"scope '{scope}' 未绑定宿主，无法渲染");

        var host = handle.Control;
        if (host == null)
            return Reject(scope, configPath, "host-control", $"宿主 '{scope}' 的控件未注册到 ControlPool，无法渲染");

        // ② 配置加载
        var config = await _configLoader(configPath).ConfigureAwait(false);
        if (config == null)
            return Reject(scope, configPath, "config-load", "配置加载失败");

        // ②·F1 渲染宿主循环防护（RenderHostCycleGuard）：状态管线渲染段唯一权威闸门。
        // 命中"配置声明了它正在渲染进入的作用域/宿主控件"→ 拒绝渲染，Succeeded=false + 校验文案，
        // 由 ScopeLifecycleStateMachine 落 Failed 终态，走既有错误链路，绝不进入 ⑤ UI 渲染——
        // 从源头阻断"壳误载为内容"的循环宿主注入（不再触发 WinForms 循环控件引用异常）。
        var cycleGuard = RenderHostCycleGuard.Validate(config, scope, handle);
        if (!cycleGuard.Allowed)
            return Reject(scope, configPath, "cycle-guard", cycleGuard.Error ?? "循环宿主注入已拦截");

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
        // E-02：宿主关闭时 InvokeAsync 以 ObjectDisposedException 明确失败，此处显式分支落 Failed，
        // 不再解引用 default! 产生 NRE（也不会被上层误判成功）。
        RenderRunResult result;
        try
        {
            result = await _uiDispatcher.InvokeAsync(
                () => _gate.LoadConfigAndRenderAsync(config, scope, host)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            FrontEndDrivenLog.Log("ScopeLifecycleAdapter_render_host_closed",
                new { scope, configPath, error = ex.Message });
            return new ScopeLoadResult(scope, false, 0, 0, $"宿主窗口已关闭，无法在 UI 线程渲染：{ex.Message}");
        }

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

    /// <summary>
    /// 渲染段拒绝出口（单一实现）：失败原因必须可见——早退分支此前无任何日志，
    /// 使「首屏空白」只能靠读代码推演。统一落到 ScopeLifecycleAdapter_render_rejected。
    /// </summary>
    private static ScopeLoadResult Reject(string scope, string configPath, string stage, string error)
    {
        FrontEndDrivenLog.Log("ScopeLifecycleAdapter_render_rejected",
            new { scope, configPath, stage, error });
        return new ScopeLoadResult(scope, false, 0, 0, error);
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
    /// initCommands 执行（initCommands → lifecycle.onAfterLoad 迁移期双支持）：
    /// 收敛为 <see cref="InitCommandExecutor"/> 的薄委托，与 lifecycle 钩子共用同一命令语义
    /// （裸函数名 / `exec &lt;Func&gt; [k=v...] output=&lt;Control&gt;`）。
    /// </summary>
    private Task ExecuteInitCommandsAsync(UIConfiguration config, string scope)
        => config.InitCommandSpecs is { Count: > 0 }
            ? InitCommandExecutor.ExecuteSpecsAsync(_platform, config.InitCommandSpecs, scope)   // B3：带 requires 相位门 + pending 重放
            : InitCommandExecutor.ExecuteAllAsync(_platform, config.InitCommands, scope);        // 旧字符串通道（零回归）

}
