#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Share.SurfacePaint.Diagnostics;

namespace Share.SurfacePaint.Compat;

/// <summary>单层成本（累计耗时 + 采样次数）。</summary>
public readonly record struct SkinLayerCost(double AvgMs, long Count);

/// <summary>单次采样快照（宿主兼容视图；实现单源 = <see cref="SurfaceRenderProfile"/>）。</summary>
public sealed record SkinRenderProfileSnapshot(
    long Paints,
    long Hits,
    long Misses,
    long Failures,
    double HitRate,
    double AvgPaintMs,
    double AvgMissMs,
    double AvgRenderMs,
    double AvgSnapshotMs,
    double AvgBlurMs,
    IReadOnlyDictionary<string, SkinLayerCost> Layers)
{
    /// <summary>单行证据（报告/日志；键序固定）。</summary>
    public string Describe()
    {
        var layers = Layers.Count == 0
            ? "-"
            : string.Join(",", Layers.OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => string.Format(CultureInfo.InvariantCulture, "{0}={1:0.00}ms/{2}",
                    pair.Key, pair.Value.AvgMs, pair.Value.Count)));
        return string.Format(CultureInfo.InvariantCulture,
            "paints={0} hits={1} misses={2} failures={3} hitRate={4:0.0}% avgPaint={5:0.00}ms avgMiss={6:0.00}ms avgRender={7:0.00}ms avgSnapshot={8:0.00}ms avgBlur={9:0.00}ms layers={10}",
            Paints, Hits, Misses, Failures, HitRate, AvgPaintMs, AvgMissMs, AvgRenderMs, AvgSnapshotMs, AvgBlurMs, layers);
    }
}

/// <summary>绘制成本采样（宿主兼容门面）：实现单源 = <see cref="SurfaceRenderProfile"/>。</summary>
public static class SkinRenderProfile
{
    /// <summary>采样开关（关闭即清空累计）。</summary>
    public static bool Enabled
    {
        get => SurfaceRenderProfile.Enabled;
        set => SurfaceRenderProfile.Enabled = value;
    }

    /// <summary>清空累计。</summary>
    public static void Reset() => SurfaceRenderProfile.Reset();

    /// <summary>当前累计快照（映射为宿主视图类型）。</summary>
    public static SkinRenderProfileSnapshot Snapshot()
    {
        var snapshot = SurfaceRenderProfile.Snapshot();
        var layers = snapshot.Layers.ToDictionary(
            pair => pair.Key,
            pair => new SkinLayerCost(pair.Value.AvgMs, pair.Value.Count),
            System.StringComparer.Ordinal);
        return new SkinRenderProfileSnapshot(
            snapshot.Paints,
            snapshot.Hits,
            snapshot.Misses,
            snapshot.Failures,
            snapshot.HitRate,
            snapshot.AvgPaintMs,
            snapshot.AvgMissMs,
            snapshot.AvgRenderMs,
            snapshot.AvgSnapshotMs,
            snapshot.AvgBlurMs,
            layers);
    }

    /// <summary>开始一次采样。</summary>
    public static long Begin() => SurfaceRenderProfile.Begin();

    /// <summary>记录一次绘制。</summary>
    public static void RecordPaint(long begin, bool hit) => SurfaceRenderProfile.RecordPaint(begin, hit);

    /// <summary>记录失败。</summary>
    public static void RecordFailure() => SurfaceRenderProfile.RecordFailure();

    /// <summary>记录渲染耗时。</summary>
    public static void RecordRender(long begin) => SurfaceRenderProfile.RecordRender(begin);

    /// <summary>记录快照耗时。</summary>
    public static void RecordSnapshot(long begin) => SurfaceRenderProfile.RecordSnapshot(begin);

    /// <summary>记录模糊耗时。</summary>
    public static void RecordBlur(long begin) => SurfaceRenderProfile.RecordBlur(begin);

    /// <summary>记录单层耗时。</summary>
    public static void RecordLayer(string primitive, long begin) => SurfaceRenderProfile.RecordLayer(primitive, begin);
}
