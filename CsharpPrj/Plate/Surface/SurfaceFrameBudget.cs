#nullable enable

namespace UiBackend;

/// <summary>帧质量档（决定像素上限 / 模糊半径 / 回读节流）。</summary>
public enum SurfaceFrameQuality
{
    /// <summary>最小档（只保底）。</summary>
    Minimal = 0,

    /// <summary>平衡档（默认）。</summary>
    Balanced = 1,

    /// <summary>全量档。</summary>
    Full = 2,
}

/// <summary>
/// 帧预算（**质量档 → 能力上限**的唯一映射）：玻璃选层与引擎帧频率都读它。
/// <para>缺省 = <see cref="Default"/>（balanced）；预算不足时选层降级由 <see cref="SurfaceGlassSpec.Clamp"/> 单一裁决。</para>
/// </summary>
/// <param name="Quality">质量档。</param>
/// <param name="MaxPixels">单帧像素上限（宽×高）。</param>
/// <param name="MaxBlurSigma">模糊半径上限（DIP）。</param>
/// <param name="IntervalMs">最小帧间隔（节流）。</param>
/// <param name="MaxReadbackLatencyMs">回读延迟上限（超限降档）。</param>
public sealed record SurfaceFrameBudget(
    SurfaceFrameQuality Quality,
    int MaxPixels,
    float MaxBlurSigma,
    int IntervalMs,
    int MaxReadbackLatencyMs)
{
    /// <summary>平衡档缺省预算。</summary>
    public static readonly SurfaceFrameBudget Default = new(
        SurfaceFrameQuality.Balanced, 3840 * 2160, 18f, 1000, 40);

    /// <summary>该预算可达的玻璃上限。</summary>
    public GlassTier MaxTier => Quality switch
    {
        SurfaceFrameQuality.Minimal => GlassTier.T0,
        SurfaceFrameQuality.Balanced => GlassTier.T2,
        SurfaceFrameQuality.Full => GlassTier.T4,
        _ => GlassTier.T0,
    };
}
