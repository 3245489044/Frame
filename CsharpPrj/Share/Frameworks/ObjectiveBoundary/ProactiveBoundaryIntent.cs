namespace Share.ObjectiveBoundary;

/// <summary>
/// 主动式边界意图：执行智能体自评文本 / L2 生成器结构化输出产出的边界动作提议（皮层主动触发）。
/// 仅提议，是否生效由 ProactiveBoundaryTrigger 门控 + BoundaryTriggerArbiter 仲裁决定。
/// </summary>
public sealed record ProactiveBoundaryIntent(
    string Action,       // 取值域：BoundaryActionKind（compact/resubmit_with_info/rescope/escalate）
    string Source,       // "text"（执行智能体自评文本）| "llm"（L2 生成器结构化输出）
    double Confidence);  // 0..1 自评置信度
