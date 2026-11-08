#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Share.SurfacePaint.Diagnostics;

/// <summary>单层成本（累计耗时 + 采样次数）。</summary>
/// <param name="AvgMs">该图元平均耗时（毫秒）。</param>
/// <param name="Count">采样次数。</param>
public readonly record struct SurfaceLayerCost(double AvgMs, long Count);

/// <summary>单次采样快照（纯数据；断言/报告只读本结构）。</summary>
/// <param name="Paints">绘制次数。</param>
/// <param name="Hits">缓存命中次数（只贴位图）。</param>
/// <param name="Misses">缓存失效次数（重建位图）。</param>
/// <param name="Failures">绘制失败次数。</param>
/// <param name="HitRate">命中率（Hits/Paints）。</param>
/// <param name="AvgPaintMs">单次绘制（含命中/失效）平均耗时。</param>
/// <param name="AvgMissMs">单次缓存失效平均耗时（含快照+重建+贴图）。</param>
/// <param name="AvgRenderMs">单次位图重建平均耗时（Render，含快照）。</param>
/// <param name="AvgSnapshotMs">单次父级快照平均耗时。</param>
/// <param name="AvgBlurMs">单次快照模糊平均耗时。</param>
/// <param name="Layers">按图元的层成本。</param>
public sealed record SurfaceRenderProfileSnapshot(
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
    IReadOnlyDictionary<string, SurfaceLayerCost> Layers)
{
    /// <summary>单行证据（报告/日志；键序固定）。</summary>
    public string Describe()
    {
        var layers = Layers.Count == 0
            ? "-"
            : string.Join(",", Layers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Format(CultureInfo.InvariantCulture, "{0}={1:0.00}ms/{2}",
                    pair.Key, pair.Value.AvgMs, pair.Value.Count)));
        return string.Format(CultureInfo.InvariantCulture,
            "paints={0} hits={1} misses={2} failures={3} hitRate={4:0.0}% avgPaint={5:0.00}ms avgMiss={6:0.00}ms avgRender={7:0.00}ms avgSnapshot={8:0.00}ms avgBlur={9:0.00}ms layers={10}",
            Paints, Hits, Misses, Failures, HitRate, AvgPaintMs, AvgMissMs, AvgRenderMs, AvgSnapshotMs, AvgBlurMs, layers);
    }
}

/// <summary>
/// 绘制成本采样（可选开关，默认关闭）：把「快照 / 模糊 / 各图层 / 命中率」拆成可复算的数字。
/// <para>关闭时每处仅一次 volatile 读 + 分支，不产生 Stopwatch 调用；开启后才计时与累加。</para>
/// <para>写者唯一：<see cref="SkinDecorator"/>（快照/命中）与 <see cref="SkinPaintEngine"/>（图层/模糊）。</para>
/// </summary>
public static class SurfaceRenderProfile
{
    private static volatile bool _enabled;

    /// <summary>采样开关（关闭即清空累计，避免把诊断数据混进下一次测量）。</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (!value) Reset();
            _enabled = value;
        }
    }

    private static readonly object Gate = new();
    private static long _paints;
    private static long _hits;
    private static long _misses;
    private static long _failures;
    private static long _paintTicks;
    private static long _missTicks;
    private static long _renderTicks;
    private static long _renderCount;
    private static long _snapshotTicks;
    private static long _snapshotCount;
    private static long _blurTicks;
    private static long _blurCount;
    private static readonly Dictionary<string, (long Ticks, long Count)> LayerTotals = new(StringComparer.Ordinal);

    /// <summary>清空累计（测量开始/结束）。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _paints = _hits = _misses = _failures = 0;
            _paintTicks = _missTicks = _renderTicks = _renderCount = 0;
            _snapshotTicks = _snapshotCount = _blurTicks = _blurCount = 0;
            LayerTotals.Clear();
        }
    }

    /// <summary>当前累计快照（不可变；读不阻塞绘制）。</summary>
    public static SurfaceRenderProfileSnapshot Snapshot()
    {
        lock (Gate)
        {
            var layers = LayerTotals.ToDictionary(
                pair => pair.Key,
                pair => new SurfaceLayerCost(Ms(pair.Value.Ticks, pair.Value.Count), pair.Value.Count),
                StringComparer.Ordinal);
            return new SurfaceRenderProfileSnapshot(
                _paints,
                _hits,
                _misses,
                _failures,
                _paints == 0 ? 0 : _hits * 100.0 / _paints,
                Ms(_paintTicks, _paints),
                Ms(_missTicks, _misses),
                Ms(_renderTicks, _renderCount),
                Ms(_snapshotTicks, _snapshotCount),
                Ms(_blurTicks, _blurCount),
                layers);
        }
    }

    /// <summary>计时起点（关闭 ⇒ 0，调用方跳过计时分支）。</summary>
    public static long Begin() => _enabled ? Stopwatch.GetTimestamp() : 0;

    /// <summary>记录一次绘制（命中/失效）。</summary>
    public static void RecordPaint(long begin, bool hit)
    {
        if (begin == 0) return;
        var ticks = Stopwatch.GetTimestamp() - begin;
        lock (Gate)
        {
            _paints++;
            _paintTicks += ticks;
            if (hit) _hits++;
            else
            {
                _misses++;
                _missTicks += ticks;
            }
        }
    }

    /// <summary>记录一次绘制失败（异常隔离路径）。</summary>
    public static void RecordFailure()
    {
        if (!_enabled) return;
        lock (Gate) _failures++;
    }

    /// <summary>记录一次位图重建（Render，含快照与全部静态层）。</summary>
    public static void RecordRender(long begin)
    {
        if (begin == 0) return;
        var ticks = Stopwatch.GetTimestamp() - begin;
        lock (Gate)
        {
            _renderCount++;
            _renderTicks += ticks;
        }
    }

    /// <summary>记录一次父级快照。</summary>
    public static void RecordSnapshot(long begin)
    {
        if (begin == 0) return;
        var ticks = Stopwatch.GetTimestamp() - begin;
        lock (Gate)
        {
            _snapshotCount++;
            _snapshotTicks += ticks;
        }
    }

    /// <summary>记录一次快照模糊（降采样 + 升采样）。</summary>
    public static void RecordBlur(long begin)
    {
        if (begin == 0) return;
        var ticks = Stopwatch.GetTimestamp() - begin;
        lock (Gate)
        {
            _blurCount++;
            _blurTicks += ticks;
        }
    }

    /// <summary>记录一个图元的绘制耗时。</summary>
    public static void RecordLayer(string primitive, long begin)
    {
        if (begin == 0) return;
        var ticks = Stopwatch.GetTimestamp() - begin;
        lock (Gate)
        {
            var current = LayerTotals.TryGetValue(primitive, out var value) ? value : (Ticks: 0L, Count: 0L);
            LayerTotals[primitive] = (current.Ticks + ticks, current.Count + 1);
        }
    }

    private static double Ms(long ticks, long count)
        => count == 0 || Stopwatch.Frequency == 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / count;
}
