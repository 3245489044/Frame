using Strategy.Observability.Ports;

namespace Strategy.Observability.Projection;

/// <summary>
/// 数值聚合投影（E13）：单度量键在事件集上的计数 / 和 / 均值 / 极值
/// （<c>Aggregate(key)</c> 取值根）。空集时极值为 null（不伪造 0）。
/// </summary>
public sealed class AggregateProjection
{
    private AggregateProjection(string metricKey, long count, double sum, double? max, double? min)
    {
        MetricKey = metricKey;
        Count = count;
        Sum = sum;
        Max = max;
        Min = min;
    }

    /// <summary>度量键。</summary>
    public string MetricKey { get; }

    /// <summary>参与聚合的样本数。</summary>
    public long Count { get; }

    /// <summary>总和。</summary>
    public double Sum { get; }

    /// <summary>均值（空集 → 0）。</summary>
    public double Mean => Count == 0 ? 0 : Sum / Count;

    /// <summary>最大值（空集 → null）。</summary>
    public double? Max { get; }

    /// <summary>最小值（空集 → null）。</summary>
    public double? Min { get; }

    /// <summary>是否空集。</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>在事件集上聚合某度量键（缺该度量的样本跳过）。</summary>
    public static AggregateProjection Over(string metricKey, IEnumerable<ObservationOutcome> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);
        PortGuard.Required(source, nameof(source));
        return Over(metricKey, source.Select(e => e.Metric(metricKey)).Where(v => v.HasValue).Select(v => v!.Value));
    }

    /// <summary>在数值序列上聚合（累加口径单源 = <see cref="PortValues.NumericStats"/>）。</summary>
    public static AggregateProjection Over(string metricKey, IEnumerable<double> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);
        PortGuard.Required(values, nameof(values));

        var stats = PortValues.NumericStats.Over(values);
        return new AggregateProjection(metricKey, stats.Count, stats.Sum, stats.Max, stats.Min);
    }
}
