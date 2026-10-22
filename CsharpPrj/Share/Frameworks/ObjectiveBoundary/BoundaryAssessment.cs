namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界评估结果（纯数据）。承载空间边界解析（有效根/是否允许）与逻辑边界评估
/// （三压力 + 加权 Score ≈ 文档 Φ(Ω,J;Context) 的工程近似）+ 是否触发重构 + 建议动作。
/// Channel/TriggerKind 标注来源通道与种类，供仲裁器与观测识别脊神经/皮层触发。
/// </summary>
public sealed record BoundaryAssessment(
    bool Triggered,
    double Score,
    double ContextPressure,
    double InfoGainRate,
    double ComplexityPressure,
    string Action,
    string? Reason,
    string? EffectiveRoot,
    bool SpatialAllowed,
    string? SpatialDenyReason,
    double PhiOld,
    TriggerChannel Channel = TriggerChannel.Reactive,
    BoundaryTriggerKind TriggerKind = BoundaryTriggerKind.ContextPressure,
    bool HysteresisHeld = false)
{
    /// <summary>健康（无触发、空间允许）的静态空评估。</summary>
    public static BoundaryAssessment Healthy { get; } =
        new(false, 0d, 0d, 1d, 0d, BoundaryActionKind.None, null, null, true, null, 0d);

    public BoundaryAssessment WithPhi(double phi) => this with { PhiOld = phi };
}
