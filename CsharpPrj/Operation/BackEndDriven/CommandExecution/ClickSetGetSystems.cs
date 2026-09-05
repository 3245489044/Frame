#nullable enable

using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using BackEndDriven.Diagnostics;
using Modules.Threading;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// click/doubleclick 命令（M3·事件与命令收编）：发 ECS 事件（IInteractionEventPort），
/// 与 GUI 人手点击走同一条 ECS 事件链（EcsEventBus → EventBindingsComponent → FunctionCallFacade）。
/// 不再 PerformClick 直操作物理控件——「控件在树即可点」假阳性根因之一，且命令与渲染链彻底解耦。
/// </summary>
public class ClickSystem : CommandSystemBase, IAsyncCommandSystem
{
    private readonly IInteractionEventPort? _eventPort;
    private readonly IEcsStateAccessor? _stateAccessor;

    /// <summary>反应式等待默认超时（对齐 FunctionCallFacade / 管线默认 30s；权威常量见 <see cref="InteractionDefaults"/>）。</summary>
    public const int DefaultWaitTimeoutMs = InteractionDefaults.DefaultFunctionTimeoutMs;

    /// <summary>
    /// 等待宽限（P5·归因可读）：click 等待预算 = 函数执行预算 + 宽限，
    /// 使 click 能在函数于预算点被内核终止后、于完成信号上读到归因分类（[FrameworkTimedOut] 等），
    /// 而非与预算竞速后落入笼统「等待函数完成超时」。
    /// </summary>
    public const int WaitGraceMs = 5_000;

    public ClickSystem(EntityManager entityManager)
        : base(entityManager, "click", "press", "tap", "doubleclick")
    {
        _eventPort = AppServiceProvider.Resolve<IInteractionEventPort>();
        _stateAccessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
    }

    /// <summary>同步路径（legacy Update 管线）委托异步实现；主脚本路径（ExecuteAllAsync → IAsyncCommandSystem）走非阻塞异步。</summary>
    public override void Execute(Entity entity, CommandMetaComponent meta)
        => ExecuteAsync(entity, meta).GetAwaiter().GetResult();

    /// <summary>
    /// 异步命令实现（M1·命令层 async 化）：命令线程不再同步阻塞等待异步长任务（真实 LLM 秒-分钟级），
    /// 经 <see cref="OperationExtensions.TryWaitAsync"/>（底层 Task.WaitAsync，非阻塞）订阅 AsyncOp 完成信号，
    /// 慢 LLM 调用期间让出命令线程、GUI/消息泵/回写可继续。
    /// </summary>
    public async Task ExecuteAsync(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "click: 缺少目标控件名");
            return;
        }

        if (_eventPort == null)
        {
            SetFailure(entity, $"{meta.Action}: IInteractionEventPort 未注册（FrontEndDriven 管线未装配）");
            return;
        }

        // 反应式等待参数（R）：wait=true → 在事件函数真实完成信号（EventDispatchResult.Completion）上
        // 阻塞等待——非轮询，替代脚本层「sleep + await <control> timeout」的时间启发式。
        var wait = ParseBool(meta.Parameters, "wait");
        var (hasTimeout, parsedTimeoutMs) = ParseTimeout(meta.Parameters, "timeout");
        var functionTimeoutMs = hasTimeout ? parsedTimeoutMs : (int?)null;
        // 等待预算 = 函数预算 + 宽限：函数在预算点被终止后，click 仍能读到归因分类（而非竞速超时）。
        var waitTimeoutMs = (hasTimeout ? parsedTimeoutMs : DefaultWaitTimeoutMs) + WaitGraceMs;
        var outputTarget = ReadParam(meta.Parameters, "output");

        var isDouble = string.Equals(meta.Action, "doubleclick", StringComparison.OrdinalIgnoreCase);
        var eventName = isDouble ? "doubleclick" : "click";

        try
        {
            // M3·命令与 GUI 同链：事件入 ECS 队列并即时分发（不再直操作物理控件）。P4·Enqueue 返回分发确认。
            var ack = _eventPort.Enqueue(target, eventName, functionTimeoutMs: functionTimeoutMs);
            if (ack.Outcome != EventDispatchOutcome.Dispatched)
            {
                var reason = ack.Outcome switch
                {
                    EventDispatchOutcome.EntityNotFound
                        => $"click: 未找到控件实体 '{target}'（配置可能未渲染进 ECS 渲染世界）",
                    EventDispatchOutcome.NoBinding
                        => $"click: '{target}' 无匹配事件绑定（{eventName}）",
                    EventDispatchOutcome.Dispatching
                        => $"click: '{target}' 事件已入队（分发重入，结果待外层消费）",
                    _ => $"click: '{target}' 事件未分发（{ack.Outcome}）"
                };
                BackEndLog.Log("Command_Click_NotDispatched", new { target, outcome = ack.Outcome.ToString() });
                SetFailure(entity, reason, CommandOutcome.EventNotDispatched, target);
                return;
            }
            // R·反应式等待：在事件函数真实完成信号上阻塞（与 AwaitValueSystem 同范式），非轮询。
            if (wait)
            {
                if (ack.Completion == null)
                {
                    SetFailure(entity,
                        $"click '{target}': 事件被重入分发，无法同步等待函数完成（请改用普通 click + await）",
                        CommandOutcome.EventNotDispatched, target);
                    return;
                }

                // waitTimeoutMs <= 0 → 不设总时限：依赖函数自身空闲看门狗自终止，完成后 Completion 解阻塞。
                // M1·非阻塞：经 TryWaitAsync（Task.WaitAsync，底层 await 非线程阻塞）订阅完成信号，命令线程让出。
                var finished = await ack.Completion.TryWaitAsync(
                    waitTimeoutMs > 0
                        ? TimeSpan.FromMilliseconds(waitTimeoutMs)
                        : Timeout.InfiniteTimeSpan).ConfigureAwait(false);
                if (!finished)
                {
                    // 等待超时（函数未在预算点前终止）：给出可读原因（函数名 + 预算 + 已等待时长），
                    // 不再指向内部日志标签；opId 用于跨层关联（click-wait ↔ 内核终结事件）。
                    var fnName = ack.FunctionName ?? target;
                    BackEndLog.Log("Command_Await_Timeout",
                        new { target, functionName = fnName, functionTimeoutMs, timeoutMs = waitTimeoutMs, opId = ack.OpId, key = ack.OpId.ToString() });

                    // Fix·双时钟对齐：命令等待超时 → 立即终止在途函数（CallerToken → 内核 op → ClineClient 杀进程树），
                    // 杜绝「命令已 FAIL、函数仍后台烧满预算」的孤儿窗口。函数被提前取消后由 EcsEventBus 异步落定失败告警。
                    ack.Cancel?.Invoke();

                    SetFailure(entity,
                        $"click '{target}' 失败: 函数 '{fnName}' 超过执行预算仍未完成（已等待 {waitTimeoutMs}ms，" +
                        $"内核正在终止该调用；opId={ack.OpId}）");
                    return;
                }

                // 完成信号已落定 → 非阻塞取回结果（TryWaitAsync 返回 true 即已完成，不会阻塞命令线程）。
                var outcome = await ack.Completion.ConfigureAwait(false);
                if (!outcome.Succeeded)
                {
                    // 归因分类：FunctionFault=函数错；FrameworkTimedOut/IdleHung=框架执行层；SignalLost=框架缺陷。
                    var fnName = ack.FunctionName ?? target;
                    var detail = !string.IsNullOrWhiteSpace(outcome.Error) ? $"：{outcome.Error}" : "";
                    SetFailure(entity,
                        $"click '{target}' 失败: 函数 '{fnName}' 执行失败 [{outcome.Kind}]（opId={ack.OpId}）{detail}");
                    return;
                }

                // 函数成功且输出已回写 OutputTarget；若命令显式给 output，回读作为命令结果值。
                var value = (!string.IsNullOrWhiteSpace(outputTarget) && _stateAccessor != null)
                    ? _stateAccessor.GetValue(outputTarget)
                    : null;
                BackEndLog.Log("Command_ClickWait_Success", new { target, output = value });
                SetSuccess(entity, value, CommandOutcome.Ok, target);
                return;
            }

            BackEndLog.Log(isDouble ? "Command_DoubleClick_Success" : "Command_Click_Success",
                new { target });
            SetSuccess(entity, target);
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"{meta.Action} '{target}' 失败: {ex.Message}");
        }
    }
    private static bool ParseBool(Dictionary<string, object?>? parameters, string key)
    {
        if (parameters != null && parameters.TryGetValue(key, out var v) && v != null)
        {
            var s = v.ToString();
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1";
        }
        return false;
    }

    private static (bool Present, int Value) ParseTimeout(Dictionary<string, object?>? parameters, string key)
    {
        if (parameters != null && parameters.TryGetValue(key, out var v) && v != null
            && int.TryParse(v.ToString(), out var parsed))
        {
            return (true, parsed);   // parsed <= 0 → 不设总时限（无绝对上限，依赖函数内部空闲看门狗）
        }
        return (false, 0);
    }

    private static string? ReadParam(Dictionary<string, object?>? parameters, string key)
    {
        if (parameters != null && parameters.TryGetValue(key, out var v) && v != null)
        {
            return v.ToString();
        }
        return null;
    }

}


/// <summary>
/// set 命令（M3·事件与命令收编）：写 ECS 状态（IEcsStateAccessor → ControlValueComponent）→ 投影物理控件。
/// 不再直写物理控件（命令与渲染链解耦；参数绑定链读 ECS 状态）。
/// </summary>
public class SetSystem : CommandSystemBase
{
    private readonly IEcsStateAccessor? _stateAccessor;

    public SetSystem(EntityManager entityManager)
        : base(entityManager, "set")
    {
        _stateAccessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "set: 缺少目标控件名");
            return;
        }

        // set 值来源唯一为显式 value 参数。移除「取第一个非 _ 参数当 value」的历史兼容死路径：
        // 当前模板/脚本统一用 value；保留该 fallback 会在某条 set 带多个普通参数时误取第一个当 value（三.5 隐患）。
        // 缺失 value 时 fail-fast 报错，杜绝静默取错。
        string? value = null;
        if (meta.Parameters != null
            && meta.Parameters.TryGetValue("value", out var val) && val != null)
        {
            value = val.ToString();
        }

        if (value == null)
        {
            SetFailure(entity, $"set: 无法确定 '{target}' 的值");
            return;
        }

        if (_stateAccessor == null)
        {
            SetFailure(entity, "set: IEcsStateAccessor 未注册（FrontEndDriven 管线未装配）");
            return;
        }

        try
        {
            // M3·真源写入：ECS 状态（ControlValueComponent）→ 投影物理控件
            if (_stateAccessor.SetValue(target, value))
            {
                BackEndLog.Log("Command_Set_Success", new { target, value });
                SetSuccess(entity, value);
            }
            else
            {
                SetFailure(entity, $"set: 未找到控件 '{target}'", CommandOutcome.TargetNotFound, target);
            }
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"set '{target}={value}' 失败: {ex.Message}");
        }
    }
}


/// <summary>
/// get 命令（M3·事件与命令收编）：读 ECS 状态（IEcsStateAccessor），不再反射物理控件。
/// EcsStateAccessor 为「ECS 真源优先 + 物理兜底」读策略，保持函数输出/GUI 输入未回灌场景的语义。
/// </summary>
public class GetSystem : CommandSystemBase
{
    private readonly IEcsStateAccessor? _stateAccessor;

    public GetSystem(EntityManager entityManager)
        : base(entityManager, "get", "read", "fetch")
    {
        _stateAccessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "get: 缺少目标控件名");
            return;
        }

        if (_stateAccessor == null)
        {
            SetFailure(entity, "get: IEcsStateAccessor 未注册（FrontEndDriven 管线未装配）");
            return;
        }

        try
        {
            var value = _stateAccessor.GetValue(target);
            if (value == null)
            {
                SetFailure(entity, $"get: 未找到控件 '{target}'", CommandOutcome.TargetNotFound, target);
                return;
            }

            BackEndLog.Log("Command_Get", new { target, value = value ?? "(空)" });
            // get 支持 output：把读回值写 ECS 并投影物理控件（如 resultTextBox），供脚本/日志核对读回内容
            // （如 htmlviewer 视图切换后的 HTML，端到端验证真实点击是否触发重渲染）。
            if (meta.Parameters != null
                && meta.Parameters.TryGetValue("output", out var outObj) && outObj is string outName
                && !string.IsNullOrWhiteSpace(outName))
            {
                _stateAccessor.SetValue(outName, value ?? "");
            }
            SetSuccess(entity, value ?? "");
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"get '{target}' 失败: {ex.Message}");
        }
    }
}
