namespace Modules.Observe.Metrics;

/// <summary>
/// 预分配指标单元：按 <see cref="MetricId"/> 下标直接访问，无字典、无注册、无反射。
/// Counter 用 Interlocked（跨线程累加）；Gauge 单写者更新 last/min/max；
/// Histogram 逐指标一份 log2 桶。所有写入只在会话开启时发生。
/// </summary>
internal sealed class MetricCells
{
    private readonly long[] _counters = new long[MetricCatalog.Count];
    private readonly long[] _gaugeLast = new long[MetricCatalog.Count];
    private readonly long[] _gaugeMin = new long[MetricCatalog.Count];
    private readonly long[] _gaugeMax = new long[MetricCatalog.Count];
    private readonly MetricHistogram?[] _histograms = new MetricHistogram?[MetricCatalog.Count];

    internal MetricCells()
    {
        for (int i = 0; i < MetricCatalog.Count; i++)
        {
            if (MetricCatalog.KindOf((MetricId)i) == MetricKind.Histogram)
                _histograms[i] = new MetricHistogram();
        }
    }

    internal void Add(MetricId id, long value) =>
        Interlocked.Add(ref _counters[(int)id], value);

    internal void Set(MetricId id, long value)
    {
        int index = (int)id;
        Volatile.Write(ref _gaugeLast[index], value);
        if (Volatile.Read(ref _gaugeMin[index]) == 0 || value < Volatile.Read(ref _gaugeMin[index]))
            Volatile.Write(ref _gaugeMin[index], value);
        if (value > Volatile.Read(ref _gaugeMax[index]))
            Volatile.Write(ref _gaugeMax[index], value);
    }

    internal void Record(MetricId id, long ticks) => _histograms[(int)id]?.Record(ticks);

    internal long CounterOf(MetricId id) => Volatile.Read(ref _counters[(int)id]);

    internal (long Last, long Min, long Max) GaugeOf(MetricId id)
    {
        int index = (int)id;
        return (Volatile.Read(ref _gaugeLast[index]),
                Volatile.Read(ref _gaugeMin[index]),
                Volatile.Read(ref _gaugeMax[index]));
    }

    internal MetricHistogram? HistogramOf(MetricId id) => _histograms[(int)id];

    internal void Reset()
    {
        Array.Clear(_counters, 0, _counters.Length);
        Array.Clear(_gaugeLast, 0, _gaugeLast.Length);
        Array.Clear(_gaugeMin, 0, _gaugeMin.Length);
        Array.Clear(_gaugeMax, 0, _gaugeMax.Length);
        for (int i = 0; i < _histograms.Length; i++)
            _histograms[i]?.Reset();
    }
}
