
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
using Modules.EventFramework.Bus;
using Plate.HtmlViewer;
using Share.EventBindingOrchestrator.Invocation;
using Share.EventBindingOrchestrator.Abstractions;
using Share.EventBindingOrchestrator.Execution;
using TConfig;
using TConfig.ECS.Components;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.ControlInteraction.EventBinding;

/// <summary>ECS 交互事件（控件名 + 事件名 + 载荷；UI 线程统一入队）。</summary>
public sealed record UIInteractionEvent(string ControlName, string EventName, object? Payload = null, int? FunctionTimeoutMs = null, DispatchAck? Ack = null);

/// <summary>
/// 逐事件分发确认单元（E2 修复）：入队方只读取自己这条事件的分发结果，
/// 不再用「最后出队事件」冒充本次 ack（并发/重入下归属错误）。
/// </summary>
public sealed class DispatchAck
{
    private readonly object _gate = new();
    private EventDispatchResult _result;
    private bool _settled;

    public EventDispatchResult? Result
    {
        get
        {
            lock (_gate) return _settled ? _result : null;
        }
    }

    public void Settle(EventDispatchResult result)
    {
        lock (_gate)
        {
            _result = result;
            _settled = true;
        }
    }
}

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

    // 单消费者可重入派发队列（Modules.EventFramework）：守卫 + 重入入队 + 统一 drain 单源。
    private readonly ReentrantEventDispatchQueue<UIInteractionEvent> _queue = new();

    /// <summary>
    /// 在途分发取消表（Fix·双时钟对齐）：opId → 可取消令牌源。
    /// 命令层（click wait=true）等待超时时经 EventDispatchResult.Cancel 取消，内核 op 经 CallerToken 链接后终止。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    /// <summary>F4.2：Failed 作用域提供者（缺省 null = 关闭聚合，走既有逐条 EntityNotFound 日志）。</summary>
    private readonly Func<IReadOnlyCollection<string>>? _failedScopeProvider;

    /// <summary>R2：页面区域写者解析（按触发元素的域限定键找拥有该页面的运行时；缺省查注册表）。</summary>
    private readonly Func<string, IPageRegionWriter?> _regionWriterResolver;

    /// <summary>F4.2：本次分发突发中已见的 EntityNotFound 命中计数（首条聚合，后续静默）。</summary>
    private int _entityNotFoundBurst;

    // R3：输出决策去重账随该日志一并删除（每次调用 ≤2 条：启动 + 终态）。

    /// <summary>按钮事件函数默认执行超时（毫秒）；命令 click-wait 的 timeout 经 Enqueue(functionTimeoutMs) 覆盖。权威常量见 <see cref="InteractionDefaults"/>。</summary>
    public const int DefaultFunctionTimeoutMs = InteractionDefaults.DefaultFunctionTimeoutMs;

    public EcsEventBus(
        EntityManager entityManager,
        IEcsStateAccessor stateAccessor,
        ControlPool controlPool,
        EntityQueryService entityQuery,
        Func<IReadOnlyCollection<string>>? failedScopeProvider = null,
        Func<string, IPageRegionWriter?>? regionWriterResolver = null)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _stateAccessor = stateAccessor ?? throw new ArgumentNullException(nameof(stateAccessor));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _entityQuery = entityQuery ?? throw new ArgumentNullException(nameof(entityQuery));
        _failedScopeProvider = failedScopeProvider;
        _regionWriterResolver = regionWriterResolver ?? PageRegionWriterRegistry.Resolve;
    }

    public int PendingCount => _queue.Count;

    public EventDispatchResult Enqueue(string controlName, string eventName, object? payload = null, int? functionTimeoutMs = null)
    {
        if (string.IsNullOrWhiteSpace(controlName) || string.IsNullOrWhiteSpace(eventName))
            return new EventDispatchResult(EventDispatchOutcome.Ignored);

        // R3：入队日志与分发启动日志重复（启动行已带参数），入队侧不再单记。

        var evt = new UIInteractionEvent(controlName, eventName, payload, functionTimeoutMs, new DispatchAck());
        _queue.Enqueue(evt);

        // 重入保护：分发中再次 Enqueue 只入队（外层分发循环会消费），同步无法确知该事件结果 → Dispatching。
        if (!_queue.TryEnterDrain())
            return new EventDispatchResult(EventDispatchOutcome.Dispatching);

        try
        {
            // 新一轮分发突发：重置聚合计数（F4.2）
            _entityNotFoundBurst = 0;

            // 命令链期望同步语义（click → 函数执行 → 结果）；GUI 采集也即时分发。
            _queue.Drain(e => e.Ack?.Settle(Dispatch(e)));

            // E2：只认本事件的逐事件 ack（原实现返回「最后出队事件」结果，并发下归属错）。
            return evt.Ack?.Result ?? new EventDispatchResult(EventDispatchOutcome.Ignored);
        }
        finally
        {
            _queue.ExitDrain();
        }
    }

    public void DispatchPending()
    {
        if (!_queue.TryEnterDrain()) return;

        try
        {
            _entityNotFoundBurst = 0; // F4.2：新一轮突发重置聚合计数
            _queue.Drain(evt => evt.Ack?.Settle(Dispatch(evt)));
        }
        finally
        {
            _queue.ExitDrain();
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

            // R3：启动日志已携带参数，原独立 EcsEventBus_Dispatch 行删除（每次调用 ≤2 条：启动 + 终态）。

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
        // D2：预算单源（InteractionDefaults）；0/负 = 不设绝对上限。
        var budget = (functionTimeoutMs ?? DefaultFunctionTimeoutMs) > 0
            ? (functionTimeoutMs ?? DefaultFunctionTimeoutMs)
            : (int?)null;
        try
        {
            // D1·单入口：GUI/命令事件与脚本 exec 共用 FunctionCallFacade（内核唯一执行体）。
            // 结果携带结构化归因（Outcome）与原始输出（RawOutput），输出落地不退化文本。
            var request = new FunctionCallRequest
            {
                FunctionName = binding.FunctionName,
                Parameters = parameters,
                Source = ToFunctionCallSource(evt.EventName),
                Mode = ExecutionMode.Background,
                TimeoutMs = budget ?? 0,
                OutputTarget = binding.OutputTarget,
                CorrelationId = opId == Guid.Empty ? null : opId,
                // Fix·双时钟对齐：把命令层可取消令牌透传内核 op，命令等待超时可提前终止在途函数。
                CallerToken = cts?.Token
            };

            // 诊断：确认传给内核的预算（函数执行超时预算 = click 的 timeout 或默认值）。
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_start",
                new { opId, controlName = evt.ControlName, eventName = evt.EventName,
                      functionName = binding.FunctionName, budgetMs = budget, parameters });

            var result = await FunctionCallFacade.ExecuteAsync(request).ConfigureAwait(false);

            // 原始输出（RenderOutput 落地计划）不经文本投影丢失；内核已 await，不再残留 Task 需解包（R8 删除）。
            var effectiveOutput = result.RawOutput;

            // 落地判据单点：目标是否可落地统一由 PageWriteRouting 裁决（此处不再重复判 output 是否为空）。
            if (result.IsSuccess && effectiveOutput != null)
            {
                try
                {
                    await WriteOutputLandingAsync(binding, effectiveOutput, opId, evt).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // E5：写回阶段异常不得归因为函数错（阶段化归因）。
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_failed",
                        new
                        {
                            opId,
                            controlName = evt.ControlName,
                            functionName = binding.FunctionName,
                            stage = "OutputWrite",
                            outputTarget = binding.OutputTarget,
                            error = ex.Message
                        });
                    op?.Complete(new EventFunctionOutcome(false, EventFunctionResultKind.OutputWriteFault, ex.Message));
                    return;
                }

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
                            error = result.ErrorMessage ?? "完成信号未落定",
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
                        error = result.ErrorMessage ?? "未知错误（函数未注册或执行失败）",
                        hint = BudgetHint(result.Outcome)
                    });
                op?.Complete(new EventFunctionOutcome(false, ToKind(result.Outcome), result.ErrorMessage));
            }
            else
            {
                // 成功但无输出目标/输出为空：命令仍视为完成（成功），等待方解阻塞。
                // 输出联动断点告警：显式 Skip（不写）与真无输出可区分（Skip 是合法决策，不是断点）。
                if (!string.IsNullOrWhiteSpace(binding.OutputTarget))
                {
                    if (result.RawOutput is RenderOutput { IsSkip: true })
                    {
                        FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_skip",
                            new
                            {
                                opId,
                                controlName = evt.ControlName,
                                functionName = binding.FunctionName,
                                outputTarget = binding.OutputTarget,
                            });
                    }
                    else
                    {
                        FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_empty",
                            new
                            {
                                opId,
                                controlName = evt.ControlName,
                                functionName = binding.FunctionName,
                                outputTarget = binding.OutputTarget,
                                outputType = result.RawOutput?.GetType().Name ?? "(null)"
                            });
                    }
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
                    stage = "Invoke",
                    outcome = "FrameworkFault",
                    budgetMs = budget,
                    error = ex.Message,
                    hint = string.Empty
                });
            op?.Complete(new EventFunctionOutcome(false, EventFunctionResultKind.FrameworkFault, ex.Message));
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
    private void WriteOutput(EventBindingComponent binding, string target, string? text, Guid opId, UIInteractionEvent evt)
    {
        // E10/D10：文本回写唯一经 OutputTargetWriter（写语义由 outputMode 声明），本类不再直呼写方法。
        if (!OutputTargetWriter.Write(_stateAccessor, target, text, binding.OutputMode))
            return;

        if (string.Equals(binding.OutputMode, TConfig.OutputWriteModes.Append, StringComparison.OrdinalIgnoreCase))
        {
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_appended",
                new { opId, controlName = evt.ControlName, functionName = binding.FunctionName, outputTarget = binding.OutputTarget });
        }
    }

    /// <summary>输出落地（单写落地 B3）：<see cref="RenderOutput"/> 携带写回计划且目标为 <see cref="HtmlViewer"/> 时，
    /// 经其计划执行器落地（替代对 ToString 的静默丢弃，空串不写）；否则按 outputMode 文本回写（原有语义）。
    /// 无法落地/执行异常 → 显式告警（不静默），随后回退文本写（空则跳过）。</summary>
    private async Task WriteOutputLandingAsync(EventBindingComponent binding, object effectiveOutput, Guid opId, UIInteractionEvent evt)
    {
        var hasPlan = effectiveOutput is RenderOutput { HasPlan: true };
        var regionWriter = _regionWriterResolver(evt.ControlName);
        // 目标裁决单点（PageWriteRouting）：是否可落地、落到控件/区域/页面运行时，全部在此判定，调用方不再重复判空。
        var disposition = PageWriteRouting.Resolve(
            binding.OutputTarget,
            regionWriter?.Regions,
            hasSelfDescribingPlan: hasPlan,
            pageRuntimeAvailable: regionWriter is not null);

        if (disposition.IsRejected)
        {
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_region_rejected",
                new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                      outputTarget = binding.OutputTarget, region = disposition.RegionKey, reason = disposition.Reason,
                      declared = regionWriter?.Regions?.Keys });
            return;
        }

        if (disposition.IsUnlandable)
        {
            // 计划产物被丢弃 = 真问题（Warning）；纯文本无目标 = 声明常态（Debug，不刷屏）。
            if (hasPlan)
            {
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_unresolved",
                    new { opId, controlName = evt.ControlName, functionName = binding.FunctionName, reason = disposition.Reason });
            }
            else
            {
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_no_target",
                    new { opId, controlName = evt.ControlName, functionName = binding.FunctionName, outputType = effectiveOutput.GetType().Name });
            }
            return;
        }

        if (disposition.IsPageRuntime)
        {
            // 未声明 output 但产物是自描述计划：交由触发元素所属页面运行时的单写者落地。
            var pagePlan = ((RenderOutput)effectiveOutput).Plan!;
            FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_plan_via_page",
                new
                {
                    opId,
                    controlName = evt.ControlName,
                    functionName = binding.FunctionName,
                    planKind = pagePlan.Kind.ToString(),
                    targets = pagePlan.Targets.Count
                });

            try
            {
                await regionWriter!.ExecutePlanAsync(pagePlan, opId.ToString("N")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_plan_unlanded",
                    new
                    {
                        opId,
                        controlName = evt.ControlName,
                        functionName = binding.FunctionName,
                        outputTarget = "(缺省→页面运行时)",
                        targetType = regionWriter!.GetType().Name,
                        executorPresent = true,
                        planKind = pagePlan.Kind.ToString(),
                        error = ex.Message
                    });
            }

            return;
        }
        if (effectiveOutput is RenderOutput { HasPlan: true } planOutput)
        {
            var plan = planOutput.Plan!;
            if (disposition.IsRegion)
            {
                if (regionWriter is null)
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_region_rejected",
                        new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                              outputTarget = binding.OutputTarget, reason = "无区域写者（页面运行时未装配）" });
                    return;
                }

                try
                {
                    await regionWriter.ExecutePlanAsync(plan, opId.ToString("N")).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_plan_unlanded",
                        new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                              outputTarget = binding.OutputTarget, planKind = plan.Kind.ToString(), error = ex.Message });
                }
                return;
            }

            var planTarget = disposition.Target;
            if (_controlPool.GetControl(planTarget) is HtmlViewer hv && hv.RenderPlanExecutor is { } executor)
            {
                try
                {
                    await executor(plan).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_plan_unlanded",
                        new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                              outputTarget = planTarget, error = ex.Message });
                }
            }
            else
            {
                var targetControl = _controlPool.GetControl(planTarget);
                var pageViewer = targetControl as HtmlViewer;
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_plan_unlanded",
                    new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                          outputTarget = planTarget, targetType = targetControl?.GetType().Name ?? "(null)",
                          executorPresent = pageViewer?.RenderPlanExecutor is not null,
                          planKind = plan.Kind.ToString(), error = "目标非 HtmlViewer 或未装配计划执行器" });

                if (pageViewer is not null
                    && plan.Kind == UiWriteKind.FullDocument
                    && plan.Targets.Count > 0
                    && plan.Targets[0].Payload is string fallbackHtml
                    && !string.IsNullOrWhiteSpace(fallbackHtml))
                {
                    try
                    {
                        await pageViewer.WriteContentAsync(fallbackHtml).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_plan_unlanded",
                            new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                                  outputTarget = planTarget, fallback = "WriteContentAsync", error = ex.Message });
                    }
                }
            }
            return;
        }

        if (disposition.IsRegion)
        {
            var text = effectiveOutput?.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                if (effectiveOutput is RenderOutput { IsSkip: true })
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_skip",
                        new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                              outputTarget = binding.OutputTarget });
                }
                else
                {
                    FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_empty",
                        new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                              outputTarget = binding.OutputTarget, outputType = effectiveOutput?.GetType().Name ?? "(null)" });
                }
                return;
            }

            if (regionWriter is null)
            {
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_region_rejected",
                    new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                          outputTarget = binding.OutputTarget, reason = "无区域写者（页面运行时未装配）" });
                return;
            }

            var append = regionWriter.IsStreamRegion(disposition.RegionKey!)
                         || string.Equals(binding.OutputMode, TConfig.OutputWriteModes.Append, StringComparison.OrdinalIgnoreCase);
            try
            {
                var write = await regionWriter.WriteRegionAsync(disposition.RegionKey!, text, append, opId.ToString("N")).ConfigureAwait(false);
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_region_written",
                    new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                          region = disposition.RegionKey, append, status = write.Status.ToString() });
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("EcsEventBus_Dispatch_output_region_failed",
                    new { opId, controlName = evt.ControlName, functionName = binding.FunctionName,
                          region = disposition.RegionKey, error = ex.Message });
            }
            return;
        }

        WriteOutput(binding, disposition.Target, effectiveOutput.ToString(), opId, evt);
    }
    /// <summary>
    /// GUI 事件名 → 函数调用来源（E6 修复：不再恒归 ButtonClick）。
    /// 来源词表唯一为 <see cref="FunctionCallSource"/>，未列出的事件名显式归 Other（并保留 eventName 于日志）。
    /// </summary>
    private static FunctionCallSource ToFunctionCallSource(string eventName) => eventName?.ToLowerInvariant() switch
    {
        "click" or "doubleclick" => FunctionCallSource.ButtonClick,
        "message" => FunctionCallSource.ExternalPort,
        _ => FunctionCallSource.Other
    };

    private static EventFunctionResultKind ToKind(InvocationOutcome outcome) => outcome switch
    {
        InvocationOutcome.Completed => EventFunctionResultKind.Succeeded,
        InvocationOutcome.FunctionFault => EventFunctionResultKind.FunctionFault,
        InvocationOutcome.FrameworkTimedOut => EventFunctionResultKind.FrameworkTimedOut,
        InvocationOutcome.FrameworkFault => EventFunctionResultKind.FrameworkFault,
        InvocationOutcome.FrameworkIdleHung => EventFunctionResultKind.FrameworkIdleHung,
        InvocationOutcome.ReconcileFault => EventFunctionResultKind.ReconcileFault,
        InvocationOutcome.Cancelled => EventFunctionResultKind.Cancelled,
        _ => EventFunctionResultKind.SignalLost
    };

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
                var payloadText = (srcControl as HtmlViewer)?.LastMessagePayloadJson;
                value = string.IsNullOrEmpty(payloadText) ? (evt.Payload?.ToString() ?? string.Empty) : payloadText;
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

                // E-20②：此处不得把 null 提前归一为 ""——否则下方「源缺失 → 使用 DefaultValue」分支永不可达，
                // 绑定声明的默认参数全部被空串覆盖。
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
