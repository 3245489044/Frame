using Share.AiScheduling.Regulation;
using Share.TaskStatusPool.Loops;
using System;
using Share.ObjectiveBoundary;
using Share.ObjectiveBoundary;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 脊神经反应式触发：确定性评估（LogicalBoundaryEvaluator），无 LLM、低延迟、短路优先。
/// 负责硬信号（协议硬断/token 耗尽/错误熔断）与软阈值（上下文/信息增益/复杂度）压力评估。
/// 输出附加 TriggerKind，供仲裁器识别"脊神经短路口"（硬信号立即决定）。
/// </summary>
public sealed class ReactiveBoundaryTrigger : IBoundaryTrigger
{
    public TriggerChannel Channel => TriggerChannel.Reactive;

    public BoundaryTriggerKind Kind => BoundaryTriggerKind.ContextPressure;

    public int Priority => 100;

    public BoundaryAssessment Evaluate(
        BoundarySignal signal, BoundaryConfig config, ContextEstimate estimate, int priorResultLength,
        bool wasTriggered = false)
    {
        var a = LogicalBoundaryEvaluator.Evaluate(signal, config, estimate, priorResultLength, wasTriggered);
        // 种类（TriggerKind）已由 LogicalBoundaryEvaluator 与触发判定同源产出（D5 单源，不再在此重算阈值）。
        return a with { Channel = TriggerChannel.Reactive };
    }
}
