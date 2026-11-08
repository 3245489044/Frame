#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.SurfacePaint;

/// <summary>单层绘制结果（失败/降级语义唯一枚举）。</summary>
public enum SurfacePaintOutcome
{
    /// <summary>按声明完整落笔。</summary>
    Ok,

    /// <summary>降级落笔（如 backdrop 无快照 ⇒ 近似）。</summary>
    Degraded,

    /// <summary>未落笔（声明非法/资源缺失/未实现）。</summary>
    Skipped,

    /// <summary>落笔抛异常；单层失败不中断其他层。</summary>
    Failed,
}

/// <summary>绘制范围：全部 / 仅静态 / 仅动态（Sheen）。</summary>
public enum SurfacePaintScope
{
    /// <summary>全部相位（含高光）。</summary>
    All,

    /// <summary>静态相位（Underlay/Fill/Edge/Overlay）。</summary>
    Static,

    /// <summary>仅动态相位（Sheen）。</summary>
    Dynamic,
}

/// <summary>一帧绘制报告（载体无关；宿主可映射到自己的报告类型）。</summary>
/// <param name="Total">参与本帧的层数（按相位过滤后）。</param>
/// <param name="Ok">完整落笔层数。</param>
/// <param name="Degraded">降级落笔层数。</param>
/// <param name="Skipped">跳过层数。</param>
/// <param name="Failed">异常层数。</param>
/// <param name="Details">明细（跳过/降级/失败原因）。</param>
public sealed record SurfacePaintReport(
    int Total, int Ok, int Degraded, int Skipped, int Failed, IReadOnlyList<string> Details)
{
    /// <summary>空报告（未挂载/无层）。</summary>
    public static readonly SurfacePaintReport Empty = new(0, 0, 0, 0, 0, Array.Empty<string>());

    /// <summary>至少一层完整落笔（降级只算有像素，不算画对）。</summary>
    public bool Painted => Ok > 0;

    /// <summary>实际落笔层数（完整 + 降级）。</summary>
    public int PaintedLayers => Ok + Degraded;

    /// <summary>整帧结论：Failed &gt; Degraded &gt; Ok &gt; Skipped。</summary>
    public SurfacePaintOutcome Outcome =>
        Failed > 0 ? SurfacePaintOutcome.Failed
        : Degraded > 0 ? SurfacePaintOutcome.Degraded
        : Ok > 0 ? SurfacePaintOutcome.Ok
        : SurfacePaintOutcome.Skipped;

    /// <summary>合并两段绘制（静态底图 + 动态层）：层数与明细相加。</summary>
    public SurfacePaintReport Merge(SurfacePaintReport? other)
    {
        if (other is null) return this;
        return new SurfacePaintReport(
            Total + other.Total,
            Ok + other.Ok,
            Degraded + other.Degraded,
            Skipped + other.Skipped,
            Failed + other.Failed,
            Details.Count == 0 ? other.Details
            : other.Details.Count == 0 ? Details
            : Details.Concat(other.Details).ToArray());
    }

    /// <summary>单行证据。</summary>
    public string Describe()
        => string.Format("total={0} ok={1} degraded={2} skipped={3} failed={4} details={5}",
            Total, Ok, Degraded, Skipped, Failed,
            Details.Count == 0 ? "-" : string.Join(";", Details));
}
