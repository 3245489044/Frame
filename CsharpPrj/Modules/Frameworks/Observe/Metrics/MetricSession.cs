using System.Runtime.CompilerServices;

namespace Modules.Observe.Metrics;

/// <summary>
/// 采集会话单例：开关、帧计数、跨帧 cell 数组、输出端都在这里；Engine 侧只保留自己的帧级快照结构。
/// <para>开关来源唯一：<c>ENGINE_METRICS=1</c>（默认关）。宿主也可显式 <see cref="Configure"/>。</para>
/// <para>窗口语义：自上次 <see cref="Flush"/> / <see cref="Reset"/> 起累计，flush 后清零。</para>
/// </summary>
public static class MetricSession
{
    private static readonly MetricCells Cells = new();
    private static volatile bool _enabled = ReadEnvironment();
    private static long _frames;

    public static bool Enabled => _enabled;

    public static long Frames => Volatile.Read(ref _frames);

    public static void Configure(bool enabled) => _enabled = enabled;

    /// <summary>帧首：开启时帧计数 +1；关闭时立即返回。</summary>
    public static void BeginFrame()
    {
        if (!_enabled) return;
        Interlocked.Increment(ref _frames);
    }

    /// <summary>帧尾：当前无跨帧交换需求（窗口在 flush 时结算）。</summary>
    public static void EndFrame()
    {
        if (!_enabled) return;
    }

    internal static void AddCounter(MetricId id, long value) => Cells.Add(id, value);

    internal static void SetGauge(MetricId id, long value) => Cells.Set(id, value);

    internal static void Record(MetricId id, long ticks) => Cells.Record(id, ticks);

    public static MetricSnapshot Capture()
    {
        var counters = new List<CounterSample>(MetricCatalog.Count);
        var gauges = new List<GaugeSample>(MetricCatalog.Count);
        var histograms = new List<HistogramSample>(MetricCatalog.Count);

        for (int i = 0; i < MetricCatalog.Count; i++)
        {
            var id = (MetricId)i;
            switch (MetricCatalog.KindOf(id))
            {
                case MetricKind.Counter:
                    counters.Add(new CounterSample(MetricCatalog.NameOf(id), Cells.CounterOf(id)));
                    break;
                case MetricKind.Gauge:
                    var (last, min, max) = Cells.GaugeOf(id);
                    gauges.Add(new GaugeSample(MetricCatalog.NameOf(id), last, min, max));
                    break;
                case MetricKind.Histogram:
                    var histogram = Cells.HistogramOf(id);
                    if (histogram is null) break;
                    histograms.Add(new HistogramSample(
                        MetricCatalog.NameOf(id),
                        histogram.Total,
                        histogram.PercentileMs(0.50),
                        histogram.PercentileMs(0.95),
                        histogram.PercentileMs(0.99),
                        histogram.MaxMs));
                    break;
            }
        }

        return new MetricSnapshot(
            MetricSinks.Schema,
            Frames,
            counters.ToArray(),
            gauges.ToArray(),
            histograms.ToArray());
    }

    /// <summary>输出窗口值到 JSON 并清零；返回落盘路径（关闭态返回空串）。</summary>
    public static string Flush(string? directory = null)
    {
        if (!_enabled) return string.Empty;

        var snapshot = Capture();
        var path = MetricSinks.WriteJson(snapshot, directory);
        Reset();
        return path;
    }

    /// <summary>Console 片段：与既有帧统计行同节奏（每秒一次），只读不清零。</summary>
    public static string Describe() => _enabled ? MetricSinks.FormatConsole(Capture()) : string.Empty;

    public static void Reset()
    {
        Interlocked.Exchange(ref _frames, 0);
        Cells.Reset();
    }

    private static bool ReadEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("ENGINE_METRICS");
        return value is not null
            && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
