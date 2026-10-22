using Share.AiScheduling.Regulation;
namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界触发端口：脊神经反应式与皮层主动式两通道统一抽象（并行采集，交由 BoundaryTriggerArbiter 合并）。
/// 反应式实现 = ReactiveBoundaryTrigger（确定性评估，高优先短路）；
/// 主动式实现 = ProactiveBoundaryTrigger（LLM/执行智能体自评，门控后提议）。
/// </summary>
public interface IBoundaryTrigger
{
    /// <summary>所属触发通道（Reactive=脊神经 / Proactive=皮层）。</summary>
    TriggerChannel Channel { get; }

    /// <summary>触发种类。</summary>
    BoundaryTriggerKind Kind { get; }

    /// <summary>仲裁优先级（数值越大越优先；反应式=短路，主动式=提议）。</summary>
    int Priority { get; }

    /// <summary>评估单轮边界信号，产出该通道的边界评估（未触发=Healthy）。</summary>
    BoundaryAssessment Evaluate(
        BoundarySignal signal,
        BoundaryConfig config,
        ContextEstimate estimate,
        int priorResultLength,
        bool wasTriggered = false);
}
