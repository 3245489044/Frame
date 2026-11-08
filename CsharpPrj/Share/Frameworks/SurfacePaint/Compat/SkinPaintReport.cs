#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.SurfacePaint.Compat;

/// <summary>单层绘制结果（K6：失败/降级语义唯一枚举；禁 bool + 旁路记账）。</summary>
public enum PaintOutcome
{
    /// <summary>按声明完整落笔。</summary>
    Ok,

    /// <summary>降级落笔（如 backdrop 无快照 ⇒ 近似），像素有输出但语义不完整。</summary>
    Degraded,

    /// <summary>未落笔（声明非法/资源缺失/未实现）；必须有明细原因。</summary>
    Skipped,

    /// <summary>落笔抛异常；单层失败不中断其他层，但报告必须可见。</summary>
    Failed,
}

/// <summary>
/// 一帧绘制报告（K6）：静态底图 + 动态层合并后的唯一结果面。
/// <para><see cref="Painted"/> = 至少一层 <see cref="PaintOutcome.Ok"/>（降级不算「画对」）。</para>
/// </summary>
public sealed record SkinPaintReport(
    int Total, int Ok, int Degraded, int Skipped, int Failed, IReadOnlyList<string> Details)
{
    /// <summary>空报告（未挂载/无层）。</summary>
    public static readonly SkinPaintReport Empty =
        new(0, 0, 0, 0, 0, Array.Empty<string>());

    /// <summary>至少一层完整落笔（降级只算有像素，不算画对）。</summary>
    public bool Painted => Ok > 0;

    /// <summary>实际落笔层数（完整 + 降级）。</summary>
    public int PaintedLayers => Ok + Degraded;

    /// <summary>整帧结论：Failed &gt; Degraded &gt; Ok &gt; Skipped。</summary>
    public PaintOutcome Outcome =>
        Failed > 0 ? PaintOutcome.Failed
        : Degraded > 0 ? PaintOutcome.Degraded
        : Ok > 0 ? PaintOutcome.Ok
        : PaintOutcome.Skipped;

    /// <summary>合并两段绘制（静态底图 + 动态层）：层数与明细相加。</summary>
    public SkinPaintReport Merge(SkinPaintReport? other)
    {
        if (other is null) return this;
        return new SkinPaintReport(
            Total + other.Total,
            Ok + other.Ok,
            Degraded + other.Degraded,
            Skipped + other.Skipped,
            Failed + other.Failed,
            Details.Count == 0 ? other.Details
            : other.Details.Count == 0 ? Details
            : Details.Concat(other.Details).ToArray());
    }

    /// <summary>单层异常：记 Failed 且原因可见。</summary>
    public static SkinPaintReport FailedLayer(string primitive, string detail)
        => new(1, 0, 0, 0, 1, new[] { "failed:" + primitive + ":" + detail });

    /// <summary>单行证据。</summary>
    public string Describe()
        => string.Format("total={0} ok={1} degraded={2} skipped={3} failed={4} details={5}",
            Total, Ok, Degraded, Skipped, Failed,
            Details.Count == 0 ? "-" : string.Join(";", Details));
}
