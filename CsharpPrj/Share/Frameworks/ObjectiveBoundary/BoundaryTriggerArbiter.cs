using Share.ObjectiveBoundary;


namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界触发仲裁器（小脑协调）：把并行采集的脊神经反应式与皮层主动式评估合并为单一 BoundaryAssessment。
/// 规则（对齐 clinerules GlobalDecisionKey 冲突裁决语义，Channel 轴 Reactive &gt; Proactive）：
///  1. 反应式任一触发即决定（脊神经短路；硬信号立即，软阈值亦优先于主动式，避免单轮多动作）；
///  2. 反应式健康时，采纳主动式提议（若被门控为 advisory/未触发则保持健康）。
/// </summary>
public static class BoundaryTriggerArbiter
{
    public static BoundaryAssessment Merge(
        BoundaryAssessment reactive,
        BoundaryAssessment proactive,
        BoundaryTriggerConfig config)
    {
        // 仅当反应式通道开启且已触发时，反应式决定（脊神经短路，含与主动式同动作去重 → 反应式优先）；
        // 其余（通道关闭 / 反应式健康）一律按主动式提议或健康处理（合并原重复分支 B1）。
        if (config.Reactive.Enabled && reactive.Triggered)
            return reactive;

        return proactive.Triggered ? proactive : BoundaryAssessment.Healthy;
    }
}
