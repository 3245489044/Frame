namespace Modules.Observe.Metrics;

/// <summary>稳定字段名的指标快照：审计与 bench 对比只认这一份结构。</summary>
public sealed record MetricSnapshot(
    string Schema,
    long Frames,
    CounterSample[] Counters,
    GaugeSample[] Gauges,
    HistogramSample[] Histograms);

public sealed record CounterSample(string Name, long Value);

public sealed record GaugeSample(string Name, long Last, long Min, long Max);

public sealed record HistogramSample(
    string Name,
    long Count,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);
