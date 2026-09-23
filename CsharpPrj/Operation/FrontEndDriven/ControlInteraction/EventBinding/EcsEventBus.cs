
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECS.Bridge;
using ECS.Core;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;
using ParameterResolver.Utilities;
using Modules.Threading;
using Plate.HtmlViewer;
using Share.EventBindingOrchestrator.Invocation;
using TConfig;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlInteraction.EventBinding;

/// <summary>ECS 交互事件（控件名 + 事件名 + 载荷；UI 线程统一入队）。</summary>
public sealed record UIInteractionEvent(string ControlName, string EventName, object? Payload = null, int? FunctionTimeoutMs = null);

/// <summary>
/// ECS 事件总线（M3·事件与命令收编 + P3·参数绑定恢复）：实体事件队列 + 分发。
/// 命令 click 与 GUI 人手点击（经 InputCapturePort 采集）统一入队 → 查实体 EventBindingsComponent 订阅
/// → 参数求值（property 显式路径优先 → ECS 状态）→ FunctionCallFacade 执行。
/// 实现 <see cref="IInteractionEventPort"/>（共享层接口）。
/// </summary>
public sealed class EcsEventBus : IInteractionEventPort
{
    private readonly EntityManager _entityManager;
    private readonly IEcsStateAccessor _stateAccessor;
    private readonly ControlPool _controlPool;
    private readonly EntityQueryService _entityQuery;

    private readonly ConcurrentQueue<UIInteractionEvent> _queue = new();

    private int _dispatching;

    /// <summary>
    /// 在途分发取消表（Fix·双时钟对齐）：opId → 可取消令牌源。
    /// 命令层（click wait=true）等待超时时经 EventDispatchResult.Cancel 取消，内核 op 经 CallerToken 链接后终止。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    /// <summary>F4.2：Failed 作用域提供者（缺省 null = 关闭聚合，走既有逐条 EntityNotFound 日志）。</summary>
    private readonly Func<IReadOnlyCollection<string>>? _failedScopeProvider;

    /// <summary>F4.2：本次分发突发中已见的 EntityNotFound 命中计数（首条聚合，后续静默）。</summary>
    private int _entityNotFoundBurst;

    /// <summary>
    /// 输出决策观测去重（轮询渲染稳态噪音治理）：同「控件×函数」输出指纹未变时不再逐条落行。
    /// 键空间 = 控件×函数（有界）；恢复落行时带 <c>foldedRepeats</c> 报告期间折叠条数，观测不丢「有没有变」。
    /// </summary>
    private readonly ConcurrentDictionary<string, OutputFingerprint> _lastOutputDecision = new(StringComparer.Ordinal);

    /// <summary>输出指纹：类型 + 长度 + FNV-1a 哈希（同一内容稳定可比，跨进程无需加密强度）。</summary>
    private readonly record struct OutputFingerprint(string Value, int Folded);

    /// <summary>按钮事件函数默认执行超时（毫秒）；命令 click-wait 的 timeout 经 Enqueue(functionTimeoutMs) 覆盖。权威常量见 <see cref="InteractionDefaults"/>。</summary>
    public const int DefaultFunctionTimeoutMs = InteractionDefaults.DefaultFunctionTimeoutMs;

    public EcsEventBus(
        EntityManager entityManager,
        IEcsStateAccessor stateAccessor,
        ControlPool controlPool,
        EntityQueryService entityQuery,
        Func<IReadOnlyCollection<string>>? failedScopeProvider = null)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _stateAccessor = stateAccessor ?? throw new ArgumentNullException(nameof(stateAccessor));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _entityQuery = entityQuery ?? throw new ArgumentNullException(nameof(entityQuery));
        _failedScopeProvider = failedScopeProvider;
    }

    public int PendingCount => _queue.Count;

    public EventDispatchResult Enqueue(string controlName, string eventName, object? payload = null, int? functionTimeoutMs = null)
    {
        if (string.IsNullOrWhiteSpace(controlName) || string.IsNullOrWhiteSpace(eventName))
            return new EventDispatchResult(EventDispatchOutcome.Ignored);

        FrontEndDrivenLog.Log("EcsEventBus_Enqueue",
            new { controlName, eventName, payload = payload?.ToString(), functionTimeoutMs });

        var evt = new UIInteractionEvent(controlName, eventName, payload, functionTimeoutMs);
        _queue.Enqueue(evt);

        // 重入保护：分发中再次 Enqueue 只入队（外层分发循环会消费），同步无法确知该事件结果 → Dispatching。
        if (Interlocked.Exchange(ref _dispatching, 1) == 1)
            return new EventDispatchResult(EventDispatchOutcome.Dispatching);

        try
        {
            // 新一轮分发突发：重置聚合计数（F4.2）
            _entityNotFoundBurst = 0;

            // 命令链期望同步语义（click → 函数执行 → 结果）；GUI 采集也即时分发。
            EventDispatchResult? result = null;
            while (_queue.TryDequeue(out var e))
            {
                result = Dispatch(e);
            }
            // FIFO：同线程无并发交错时，最后出队的事件即本方法入队的那个 → 其结果为该方法确认。
            return result ?? new EventDispatchResult(EventDispatchOutcome.Ignored);
        }
        finally
        {
            Interlocked.Exchange(ref _dispatching, 0);
        }
    }

    public void DispatchPending()
    {
        if (Interlocked.Exchange(ref _dispatching, 1) == 1) return;

        try
        {
            _entityNotFoundBurst = 0; // F4.2：新一轮突发重置聚合计数

            while (_queue.TryDequeue(out var evt))
            {
                Dispatch(evt);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _dispatching, 0);
        }
    }


    private EventDispatchResult Dispatch(UIInteractionEvent evt)
    {
        var entity = FindEntityByName(evt.ControlName);
        if (!entity.IsValid)
        {
            // F4.2（fail-stop 聚合）：目标作用域处于 Failed 时，把 N 条 EntityNotFound 噪音收敛成首条聚合告警，
            // 错误面从"连带"变"聚合"，不再逐条复读相同根因。
            var failedScopes = _failedScopeProvider?.Invoke();
            if (failedScopes is { Count: > 0 })
            {
                _entityNotFoundBurst++;
                if (_entityNotFoundBurst == 1)
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_scope_failed_aggregate",
                        new
                        {
                            controlName = evt.ControlName,
                            failedScopes = string.Join(",", failedScopes),
                            note = "指向 Failed 作用域的命中已跳过，避免错误面线性放大"
                        });
                }
                return new EventDispatchResult(EventDispatchOutcome.EntityNotFound);
            }

            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_entity_not_found",
                new { controlName = evt.ControlName, eventName = evt.EventName });
            return new EventDispatchResult(EventDispatchOutcome.EntityNotFound);
        }

        if (!_entityManager.TryGetComponent<EventBindingsComponent>(entity, out var bindings)
            || bindings.Bindings is not { Count: > 0 })
        {
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_no_bindings",
                new { controlName = evt.ControlName, eventName = evt.EventName, entityId = entity.Id.ToString() });
            return new EventDispatchResult(EventDispatchOutcome.NoBinding);
        }

        int matched = 0;
        string? matchedFunction = null;
        Task<EventFunctionOutcome>? completionTask = null;
        AsyncOperation<EventFunctionOutcome>? completionOp = null;
        Guid opId = Guid.Empty;
        Action? lastCancel = null;
        foreach (var binding in bindings.Bindings)
        {
            if (!string.Equals(binding.EventName, evt.EventName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(binding.FunctionName))
                continue;

            matched++;
            matchedFunction = binding.FunctionName;
            var parameters = BuildParameters(binding, evt);

            // 调用关联 ID：一次函数调用的唯一追溯键（贯穿内核/日志）。
            opId = Guid.NewGuid();

            FrontEndDrivenLog.Log("EcsEventBus_Dispatch",
                new
                {
                    opId,
                    controlName = evt.ControlName,
                    eventName = evt.EventName,
                    functionName = binding.FunctionName,
                    parameters
                });

            // R·框架级反应式等待：每个匹配绑定一个真实完成信号（AsyncOp），随 EventDispatchResult 透出，
            // 供命令层（click wait=true）在函数实际结束时解阻塞——非轮询，替代脚本层 sleep+await+timeout 猜时间窗。
            // 完成信号经 AsyncOp 单一终结（op.Complete），不再依赖「回写成功才 TrySetResult」的隐式耦合（AEF 收编）。
            completionOp = new AsyncOperation<EventFunctionOutcome>(binding.FunctionName);
            completionTask = completionOp.Completion;

            // Fix·双时钟对齐：为本次分发建立可取消令牌。命令层等待超时可经返回的 Cancel 提前终止在途函数
            // （CallerToken → 内核 op 链接令牌 → CancellationToken → ClineClient 杀进程树），杜绝孤儿窗口。
            var cts = new CancellationTokenSource();
            _pending[opId] = cts;
            lastCancel = () => { try { cts.Cancel(); } catch (ObjectDisposedException) { /* 已在完成清理中释放 */ } };

            // 统一经 InvocationKernel 执行（生命周期 + 分类 + 观测），与 GUI/命令同链；
            // 异步分发：结果回写（OutputTarget）与函数执行解耦，异常仅日志不阻断后续事件；
            // 函数执行预算透传（R·等待/执行对齐）：命令层 click-wait 的 timeout 优先，其次绑定级声明（bindings.functionTimeoutMs），
            // 皆缺省才回落框架默认（30s）——分钟级长任务（如 conversation/send agent=cline）须显式声明，否则被预算点终止。
            _ = DispatchFunctionAsync(binding, parameters, evt, completionOp, evt.FunctionTimeoutMs ?? binding.FunctionTimeoutMs, opId, cts);
        }

        if (matched == 0)
        {
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_no_match",
                new
                {
                    controlName = evt.ControlName,
                    eventName = evt.EventName,
                    entityId = entity.Id.ToString(),
                    availableEvents = string.Join(",", bindings.Bindings.Select(b => b.EventName ?? string.Empty))
                });
            return new EventDispatchResult(EventDispatchOutcome.NoBinding);
        }

        // P4 + R：已送达绑定链并启动函数调用（异步）→ Dispatched，携带真实完成信号供命令层反应式等待；
        // 函数最终成败经 Completion 承载，不在此同步承诺。Cancel 供命令层等待超时时提前终止在途函数。
        return new EventDispatchResult(EventDispatchOutcome.Dispatched, matchedFunction,
            Completion: completionTask, OpId: opId, Cancel: lastCancel);
    }

    private async System.Threading.Tasks.Task DispatchFunctionAsync(
        EventBindingComponent binding,
        Dictionary<string, object?> parameters,
        UIInteractionEvent evt,
        AsyncOperation<EventFunctionOutcome>? op = null,
        int? functionTimeoutMs = null,
        Guid opId = default,
        CancellationTokenSource? cts = null)
    {
        // 预算在 try 前解算：失败/异常路径也要能复述同一预算来源（终态行不再只报「超时」不给因）。
        var budget = (functionTimeoutMs ?? DefaultFunctionTimeoutMs) > 0
            ? (functionTimeoutMs ?? DefaultFunctionTimeoutMs)
            : (int?)null;
        try
        {
            // 直接经内核执行（入口收敛）：结构化结果带归因分类（函数错 vs 框架错）。
            var invocation = new InvocationRequest(
                binding.FunctionName,
                parameters,
                InvocationSource.ButtonClick,
                new InvocationBudget(AbsoluteMs: budget, IdleMs: null),
                OutputTarget: binding.OutputTarget,
                CorrelationId: opId == Guid.Empty ? null : opId,
                // Fix·双时钟对齐：把命令层可取消令牌透传内核 op，命令等待超时可提前终止在途函数。
                CallerToken: cts?.Token);

            // 诊断：确认传给内核的预算（函数执行超时预算 = click 的 timeout 或默认 30s）。
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_start",
                new { opId, controlName = evt.ControlName, eventName = evt.EventName,
                      functionName = binding.FunctionName, budgetMs = budget });

            var result = await InvocationKernel.ExecuteAsync(invocation).ConfigureAwait(false);

            // M3·结果契约：函数输出回写 OutputTarget（写 ECS 状态 + 投影物理控件）。
            // 加固（输出联动）：若执行层返回未解包的 Task/Task<T>，此处兜底解包，避免异步函数输出丢失。
            var effectiveOutput = result.Output is Task outTask
                ? UnwrapTaskOutput(outTask)
                : result.Output;

            // 输出决策观测（Info 级，见 fde_log_messages.json）：确认「输出值/输出目标/是否落盘」，定位输出联动断点。
            // 仅成功路径记录（失败路径由 EcsEventBus_Dispatch_function_failed 唯一承载）；
            // 且同一「控件×函数」输出未变时不重复落行——轮询型渲染（会话页每周期整页重渲染）内容是稳态的，
            // 逐条落行会把终端刷成同长度同内容。恢复落行时报告 foldedRepeats（期间折叠条数）。
            if (result.IsSuccess)
            {
                var decisionKey = $"{evt.ControlName}|{binding.FunctionName}";
                var fingerprint = Fingerprint(result.Output);
                if (_lastOutputDecision.TryGetValue(decisionKey, out var last) && last.Value == fingerprint)
                {
                    _lastOutputDecision[decisionKey] = last with { Folded = last.Folded + 1 };
                }
                else
                {
                    // 上次落行以来被折叠的重复条数（每次落行即归零；无历史/指纹未变过 → 0）。
                    var folded = last.Folded;
                    _lastOutputDecision[decisionKey] = new OutputFingerprint(fingerprint, 0);
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_decision",
                        new
                        {
                            opId,
                            controlName = evt.ControlName,
                            functionName = binding.FunctionName,
                            success = result.IsSuccess,
                            outcome = result.Outcome.ToString(),
                            outputTarget = binding.OutputTarget,
                            outputMode = binding.OutputMode,
                            outputType = result.Output?.GetType().Name ?? "(null)",
                            outputPreview = Preview(result.Output, 160),
                            foldedRepeats = folded
                        });
                }
            }

            if (result.IsSuccess
                && !string.IsNullOrWhiteSpace(binding.OutputTarget)
                && effectiveOutput != null)
            {
                WriteOutput(binding, effectiveOutput.ToString(), opId, evt);
                op?.Complete(EventFunctionOutcome.Success());
            }
            else if (!result.IsSuccess)
            {
                // 完成信号未落定归因：内核返回 SignalLost（EndPhase 非终态）时，工作流已完成但「函数调用」层信号缺失——
                // 专用日志使「工作流完成但函数调用未落定」一眼可辨（区别于普通函数失败，M6 可观测性）。
                if (result.Outcome == InvocationOutcome.SignalLost)
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_signal_lost",
                        new
                        {
                            opId,
                            controlName = evt.ControlName,
                            eventName = evt.EventName,
                            functionName = binding.FunctionName,
                            outcome = result.Outcome.ToString(),
                            error = result.Error ?? "完成信号未落定",
                            elapsedMs = result.ElapsedMs
                        });
                }
                // 失败显式告警：带 opId + 归因分类（FunctionFault / FrameworkTimedOut / FrameworkIdleHung / Cancelled）
                // + 本次预算与处置提示（终态行自带因与下一步，避免「只报超时不给因」）。
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_function_failed",
                    new
                    {
                        opId,
                        controlName = evt.ControlName,
                        eventName = evt.EventName,
                        functionName = binding.FunctionName,
                        outcome = result.Outcome.ToString(),
                        budgetMs = budget,
                        error = result.Error ?? "未知错误（函数未注册或执行失败）",
                        hint = BudgetHint(result.Outcome)
                    });
                op?.Complete(new EventFunctionOutcome(false, ToKind(result.Outcome), result.Error));
            }
            else
            {
                // 成功但无输出目标/输出为空：命令仍视为完成（成功），等待方解阻塞。
                // 输出联动断点告警：指定了输出目标但函数无输出值 → 输入框不会刷新（「选中模板无效」根因候选）。
                if (!string.IsNullOrWhiteSpace(binding.OutputTarget))
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_empty",
                        new
                        {
                            opId,
                            controlName = evt.ControlName,
                            functionName = binding.FunctionName,
                            outputTarget = binding.OutputTarget,
                            outputType = result.Output?.GetType().Name ?? "(null)"
                        });
                }
                op?.Complete(EventFunctionOutcome.Success());
            }

            // 完成观测：与 EcsEventBus_Dispatch_start 闭合，确认「函数真的返回并回写」还是「完成信号没落定」。
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_completed",
                new
                {
                    opId,
                    controlName = evt.ControlName,
                    eventName = evt.EventName,
                    functionName = binding.FunctionName,
                    success = result.IsSuccess,
                    outcome = result.Outcome.ToString(),
                    elapsedMs = result.ElapsedMs,
                });
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_function_failed",
                new
                {
                    opId,
                    controlName = evt.ControlName,
                    eventName = evt.EventName,
                    functionName = binding.FunctionName,
                    outcome = "FunctionFault",
                    budgetMs = budget,
                    error = ex.Message,
                    hint = string.Empty
                });
            op?.Complete(new EventFunctionOutcome(false, EventFunctionResultKind.FunctionFault, ex.Message));
        }
        finally
        {
            // Fix·双时钟对齐：分发完成（成功/失败/超时/取消）后释放取消令牌，避免内存与句柄泄漏。
            if (cts != null)
            {
                _pending.TryRemove(opId, out _);
                cts.Dispose();
            }
            // 完成信号 AsyncOp 已终结（op.Complete），释放资源（已终结则 Dispose 不会取消）。
            op?.Dispose();
        }
    }

    /// <summary>
    /// 输出回写（单一裁决点）：按绑定的 outputMode 选择写语义。
    /// append（终端/日志型目标）= 追加一行累积（<see cref="IEcsStateAccessor.AppendValue"/>）；
    /// 其余（缺省 replace）= 覆盖写。语义取值权威见 <see cref="TConfig.OutputWriteModes"/>。
    /// </summary>
    private void WriteOutput(EventBindingComponent binding, string? text, Guid opId, UIInteractionEvent evt)
    {
        if (string.Equals(binding.OutputMode, TConfig.OutputWriteModes.Append, StringComparison.OrdinalIgnoreCase))
        {
            var appended = _stateAccessor.AppendValue(binding.OutputTarget, text);
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_appended",
                new { opId, controlName = evt.ControlName, functionName = binding.FunctionName, outputTarget = binding.OutputTarget, appended });
            return;
        }

        _stateAccessor.SetValue(binding.OutputTarget, text);
    }

    private static EventFunctionResultKind ToKind(InvocationOutcome outcome) => outcome switch
    {
        InvocationOutcome.Completed => EventFunctionResultKind.Succeeded,
        InvocationOutcome.FunctionFault => EventFunctionResultKind.FunctionFault,
        InvocationOutcome.FrameworkTimedOut => EventFunctionResultKind.FrameworkTimedOut,
        InvocationOutcome.FrameworkIdleHung => EventFunctionResultKind.FrameworkIdleHung,
        InvocationOutcome.Cancelled => EventFunctionResultKind.Cancelled,
        _ => EventFunctionResultKind.SignalLost
    };

    /// <summary>兜底解包 Task/Task&lt;T&gt; 返回值（执行层若未解包则此处补位，修复异步函数输出丢失）。</summary>
    private static object? UnwrapTaskOutput(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 输出指纹（类型 + 长度 + FNV-1a 哈希）：用于「输出决策观测」的稳态去重。
    /// 全量内容参与哈希（一次 O(n)，每控件每周期一次，代价可忽略），避免只看长度把「等长但内容不同」误判为未变。
    /// </summary>
    private static string Fingerprint(object? value)
    {
        if (value is null) return "(null)";
        var s = value.ToString() ?? string.Empty;
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var ch in s) hash = (hash ^ ch) * prime;
            return $"{value.GetType().Name}|{s.Length}|{hash:X8}";
        }
    }

    /// <summary>预算类失败的处置提示（随失败终态行落一条，避免只报「超时」不给因/不给下一步）。</summary>
    private static string BudgetHint(InvocationOutcome outcome) => outcome switch
    {
        InvocationOutcome.FrameworkTimedOut =>
            "长任务（LLM/cline 多轮工具循环）请提高该绑定的 functionTimeoutMs，或以脚本 exec 的 timeout 驱动",
        InvocationOutcome.FrameworkIdleHung =>
            "函数在预算内无输出心跳；检查下游是否挂起（命令层取消令牌已链接至内核 op）",
        InvocationOutcome.Cancelled => "调用方取消（外部令牌）",
        _ => string.Empty
    };

    /// <summary>
    /// 输出值预览（语义摘要）：短文本原样；长文本/HTML 输出改报「长度 + 根标签 + 头部片段」，
    /// 避免整页 HTML（万级字符）把输出决策行刷成无信息量的标签串。
    /// </summary>
    private static string? Preview(object? value, int maxLen)
    {
        if (value == null) return null;
        var s = value.ToString() ?? string.Empty;
        if (s.Length <= maxLen) return s;

        var head = s.Length <= 120 ? s : s.Substring(0, 120);
        head = head.Replace("\r", " ").Replace("\n", " ");
        var rootTag = s.StartsWith("<", StringComparison.Ordinal) ? RootTagName(s) : string.Empty;
        return $"len={s.Length}{rootTag} head={head}…";
    }

    /// <summary>取 HTML 文本的首个元素名（如 <c>&lt;div class=…</c> → <c> root=&lt;div&gt;</c>；非标签/空 → 空串）。</summary>
    private static string RootTagName(string html)
    {
        var i = html.IndexOf('<');
        while (i >= 0 && i + 1 < html.Length && (html[i + 1] == '/' || html[i + 1] == '!'))
            i = html.IndexOf('<', i + 1);
        if (i < 0 || i + 1 >= html.Length) return string.Empty;

        var j = i + 1;
        while (j < html.Length && (char.IsLetterOrDigit(html[j]) || html[j] == '-' || html[j] == '_')) j++;
        if (j == i + 1) return string.Empty;
        return $" root=<{html.Substring(i + 1, j - i - 1)}>";
    }


    /// <summary>
    /// 参数求值（M3·读控件值，替代 ParameterBinder 物理控件反射）：
    /// sourceType=literal → 原义；sourceType=control（默认）→ EcsStateAccessor.GetLiveValue(source)（物理当前真值优先）；
    /// 无参数 → value=payload/控件实时值。
    /// </summary>
    private Dictionary<string, object?> BuildParameters(EventBindingComponent binding, UIInteractionEvent evt)
    {
        if (binding.Parameters is not { Length: > 0 } parameters)
        {
            return new Dictionary<string, object?>
            {
                ["value"] = evt.Payload ?? _stateAccessor.GetLiveValue(evt.ControlName) ?? string.Empty
            };
        }

        var result = new Dictionary<string, object?>(parameters.Length);
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var sourceType = p.SourceType ?? "control";
            var sourceName = p.Source ?? string.Empty;
            var targetName = p.Target ?? (string.IsNullOrEmpty(sourceName) ? $"param{i}" : sourceName);

            object? value;
            if (string.Equals(sourceType, "literal", StringComparison.OrdinalIgnoreCase))
            {
                value = sourceName;
            }
            else if (string.Equals(sourceType, "eventPayload", StringComparison.OrdinalIgnoreCase))
            {
                // htmlviewer 消息桥负载：source 缺省取触发控件，值=该 htmlviewer 最后一次 postMessage 原始 JSON
                // （field 由函数侧解析；事件参数进 EventBindingResolver 前已存于 HtmlViewer.LastMessagePayloadJson）。
                var src = string.IsNullOrEmpty(sourceName) ? evt.ControlName : sourceName;
                var srcEntity = FindEntityByName(src);
                var srcControl = srcEntity.IsValid ? _controlPool.GetControl(srcEntity) : null;
                value = (srcControl as HtmlViewer)?.LastMessagePayloadJson ?? string.Empty;
            }
            else if (string.IsNullOrEmpty(sourceName))
            {
                // 无显式 source 的 control 参数：读触发控件自身当前值（实时真值，与 GetLiveValue 语义一致）
                value = evt.Payload ?? _stateAccessor.GetLiveValue(evt.ControlName) ?? string.Empty;
            }
            else
            {
                // 交互分发实时真值读：sourceType=control 以物理控件当前值（值语义契约）为权威，
                // 避免空值/过期 ECS 状态遮蔽用户当前选择（如下拉框已选但 ECS 未回灌/为空）。
                // ECS 真源读（GetValue）仍服务 get 命令/脚本回读（set 历史优先）——两条读语义分离（GetLiveValue vs GetValue）。
                // property 显式路径（如 SelectedItems.0.Tag）仅作实时读仍为空时的嵌套属性物理兜底。
                if (!FindEntityByName(sourceName).IsValid)
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Parameter_source_missing",
                        new
                        {
                            controlName = evt.ControlName,
                            eventName = evt.EventName,
                            functionName = binding.FunctionName,
                            sourceName,
                            targetName
                        });
                }

                value = _stateAccessor.GetLiveValue(sourceName);

                if (value == null && !string.IsNullOrWhiteSpace(p.Property))
                {
                    var sourceEntity = FindEntityByName(sourceName);
                    var sourceControl = sourceEntity.IsValid ? _controlPool.GetControl(sourceEntity) : null;
                    if (sourceControl != null)
                    {
                        value = PropertyPathResolver.ReadNestedProperty(sourceControl, p.Property);
                    }
                }

                value ??= string.Empty;
            }

            if (value == null && p.DefaultValue != null)
                value = p.DefaultValue;

            result[targetName] = value ?? string.Empty;
        }

        return result;
    }

    private Entity FindEntityByName(string controlName)
        => EntityLookup.FindByName(_entityQuery, controlName);
}
