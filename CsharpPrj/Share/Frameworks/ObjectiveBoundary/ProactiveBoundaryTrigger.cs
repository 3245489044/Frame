using Share.AiScheduling.Regulation;
using Share.TaskStatusPool.Loops;
using System;
using Share.ObjectiveBoundary;
using Share.ObjectiveBoundary;
using Share.ObjectiveBoundary;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 皮层主动式触发：消费执行智能体/L2 生成器自评意图（signal.Proactive），经白名单 + 置信度门控后提议边界动作。
/// 上下文压力低时的 compact 降级为 advisory（防 LLM 过度压缩）。仅为提议，最终生效由 BoundaryTriggerArbiter 裁决。
/// </summary>
public sealed class ProactiveBoundaryTrigger : IBoundaryTrigger
{
    public TriggerChannel Channel => TriggerChannel.Proactive;

    public BoundaryTriggerKind Kind => BoundaryTriggerKind.LlmSelfAssessment;

    public int Priority => 10;

    // DD8：健康哨兵集中到 BoundaryAssessment.Healthy，经 with 派生通道属性（消除手工重建记录 D3）。
    private static readonly BoundaryAssessment HealthyProactive =
        BoundaryAssessment.Healthy with
        {
            Channel = TriggerChannel.Proactive,
            TriggerKind = BoundaryTriggerKind.LlmSelfAssessment,
        };

    public BoundaryAssessment Evaluate(
        BoundarySignal signal, BoundaryConfig config, ContextEstimate estimate, int priorResultLength,
        bool wasTriggered = false)
    {
        var cfg = config.Triggers.Proactive;
        var intent = signal.Proactive;
        if (!cfg.Enabled || intent is null) return HealthyProactive;

        // E1：主动式自评动作不得为 None（None = 边界健康无动作，触发即矛盾）。
        if (string.Equals(intent.Action, BoundaryActionKind.None, StringComparison.OrdinalIgnoreCase)) return HealthyProactive;

        // 门控 1：动作白名单（escalate 默认不在内，防过度升级）。
        if (!cfg.ActionWhitelist.Contains(intent.Action, StringComparer.OrdinalIgnoreCase)) return HealthyProactive;

        // 门控 2：置信度下限（低置信度自评不采用，避免提议抖动）。
        if (intent.Confidence < cfg.MinConfidence) return HealthyProactive;

        double contextPressure = ContextPressureOf(config, estimate);

        // 门控 3：上下文压力低且主动式请求 compact → 降级为 advisory（仅提示不触发）。
        if (cfg.AdvisoryWhenContextLow
            && string.Equals(intent.Action, BoundaryActionKind.Compact, StringComparison.OrdinalIgnoreCase)
            && contextPressure < cfg.AdvisoryContextThreshold)
        {
            return HealthyProactive with
            {
                Reason = $"主动边界[compact]因上下文压力低({contextPressure:0.00})降级为 advisory",
            };
        }

        // R7：经 HealthyProactive 派生（DD8 集中哨兵），避免手工重建 9 字段重复。
        return HealthyProactive with
        {
            Triggered = true,
            Score = contextPressure,
            ContextPressure = contextPressure,
            InfoGainRate = 1d,
            ComplexityPressure = 0d,
            Action = intent.Action,
            Reason = $"主动边界触发[{intent.Action}](源={intent.Source},置信={intent.Confidence:0.00})",
            PhiOld = contextPressure,
        };
    }

    private static double ContextPressureOf(BoundaryConfig config, ContextEstimate estimate)
        => BoundaryPressureCalculator.ContextPressureOf(estimate, config.Logical.ContextLimitChars, config.Logical.ContextWatermark);
}
