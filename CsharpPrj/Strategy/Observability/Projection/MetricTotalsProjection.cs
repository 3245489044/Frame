namespace Strategy.Observability.Projection;

/// <summary>
/// 全度量求和投影（<c>Aggregate(&lt;作用域&gt;)</c> 取值根）：该作用域内<b>全部事件的全部度量键</b>求和
/// （如 <c>token_in</c> / <c>elapsed_ms</c> 成本 KPI），供 <c>Count</c> / <c>Samples</c> / <c>Metric(键)</c> 断言。
/// <para>
/// 与 <see cref="AggregateProjection"/> 的分工：后者按<b>单度量键</b>聚合（<c>Over(metricKey, …)</c>：
/// count/sum/mean/max/min），本类型按<b>全度量键</b>出求和表，两者语义不同、互不替代。
/// </para>
/// </summary>
public sealed class MetricTotalsProjection
{
    private MetricTotalsProjection(int count, IReadOnlyDictionary<string, double> metrics, int samples)
    {
        Count = count;
        Metrics = metrics;
        Samples = samples;
    }

    /// <summary>作用域内事件条数。</summary>
    public int Count { get; }

    /// <summary>度量键 → 求和（Ordinal 键）。</summary>
    public IReadOnlyDictionary<string, double> Metrics { get; }

    /// <summary>携带度量的样本数。</summary>
    public int Samples { get; }

    /// <summary>取度量求和（未出现 → 0）。</summary>
    public double Metric(string key) => Metrics.TryGetValue(key, out var value) ? value : 0;

    /// <summary>按事件序聚合全部度量键。</summary>
    public static MetricTotalsProjection Of(IEnumerable<ObservationOutcome> events)
    {
        PortGuard.Required(events, nameof(events));

        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        var count = 0;
        var samples = 0;

        foreach (var evt in events)
        {
            count++;
            if (evt.Metrics.Count == 0) continue;

            samples++;
            foreach (var (key, value) in evt.Metrics)
                sums[key] = sums.TryGetValue(key, out var current) ? current + value : value;
        }

        return new MetricTotalsProjection(count, sums, samples);
    }
}
