using Modules.Concurrency;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界触发仲裁器（小脑协调）：把并行采集的脊神经反应式与皮层主动式评估合并为单一 BoundaryAssessment。
/// 仲裁规则已收敛到 <see cref="TriggerArbiter"/>（Modules.Concurrency，OB-03 唯一内核）：
///  1. 反应式硬信号（协议硬断 / token 耗尽 / 错误熔断）永远短路，不受 <c>Reactive.Enabled</c> 影响；
///  2. 两通道同时可触发时按 Priority 裁决（反应式 100 / 主动式 10）；
///  3. 反应式软阈值只在 <c>Reactive.Enabled=true</c> 时生效；主动式必须读 <c>Proactive.Enabled</c>；
///  4. 主动式被门控为 advisory 时保留 Reason，不丢弃。
/// 本层只做类型映射（Assessment ↔ TriggerCandidate），不再承载仲裁分支。
/// </summary>
public static class BoundaryTriggerArbiter
{
    private const string ReactiveName = "Reactive";
    private const string ProactiveName = "Proactive";

    /// <summary>反应式默认优先级（与 <see cref="ReactiveBoundaryTrigger"/> 一致）。</summary>
    public const int DefaultReactivePriority = 100;

    /// <summary>主动式默认优先级（与 <see cref="ProactiveBoundaryTrigger"/> 一致）。</summary>
    public const int DefaultProactivePriority = 10;

    public static BoundaryAssessment Merge(
        BoundaryAssessment reactive,
        BoundaryAssessment proactive,
        BoundaryTriggerConfig config)
        => Merge(reactive, proactive, config, DefaultReactivePriority, DefaultProactivePriority);

    public static BoundaryAssessment Merge(
        BoundaryAssessment reactive,
        BoundaryAssessment proactive,
        BoundaryTriggerConfig config,
        int reactivePriority,
        int proactivePriority)
    {
        if (config == null || reactive == null || proactive == null)
            return BoundaryAssessment.Healthy;

        var arbitration = TriggerArbiter.Resolve(
            new TriggerCandidate(
                ReactiveName,
                config.Reactive.Enabled,
                reactive.Triggered,
                reactivePriority,
                IsHardSignal(reactive.TriggerKind) ? TriggerUrgency.Hard : TriggerUrgency.Soft,
                reactive.Reason),
            new TriggerCandidate(
                ProactiveName,
                config.Proactive.Enabled,
                proactive.Triggered,
                proactivePriority,
                TriggerUrgency.Soft,
                proactive.Reason));

        if (arbitration.Winner == null)
            return BoundaryAssessment.Healthy;

        if (arbitration.Winner == ReactiveName)
            return reactive with
            {
                Channel = TriggerChannel.Reactive,
                Reason = arbitration.Reason ?? reactive.Reason,
            };

        return proactive with
        {
            Channel = TriggerChannel.Proactive,
            Triggered = proactive.Triggered && !arbitration.Advisory,
            Action = arbitration.Advisory ? BoundaryActionKind.None : proactive.Action,
            TriggerKind = arbitration.Advisory
                ? BoundaryTriggerKind.None
                : proactive.TriggerKind == BoundaryTriggerKind.None
                    ? BoundaryTriggerKind.LlmSelfAssessment
                    : proactive.TriggerKind,
            Reason = arbitration.Reason ?? proactive.Reason,
        };
    }

    /// <summary>硬信号（不受迟滞/通道开关影响）。</summary>
    private static bool IsHardSignal(BoundaryTriggerKind kind)
        => kind is BoundaryTriggerKind.ProtocolStop
            or BoundaryTriggerKind.TokenExhaust
            or BoundaryTriggerKind.ErrorClass;
}
