using System.Collections.Generic;

namespace Strategy.Observability.Metrics;

/// <summary>通用 Counter（E7）：线程安全计数，供按批 flush 进事件流。</summary>
public sealed class Counter
{
    private long _value;
    public void Increment(long by = 1) => Interlocked.Add(ref _value, by);
    public long Snapshot() => Interlocked.Read(ref _value);
    public long Reset() => Interlocked.Exchange(ref _value, 0);
}

/// <summary>通用 Histogram（E7）：分桶累计，快照即分桶映射。</summary>
public sealed class Histogram
{
    private readonly Dictionary<double, long> _buckets = new();
    private readonly object _gate = new();
    private long _count;
    private double _sum;

    public void Record(double value)
    {
        lock (_gate)
        {
            _buckets[value] = _buckets.TryGetValue(value, out var c) ? c + 1 : 1;
            _count++;
            _sum += value;
        }
    }

    public HistogramSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new HistogramSnapshot(
                new Dictionary<double, long>(_buckets),
                _count,
                _sum);
        }
    }

    public void Reset()
    {
        lock (_gate) { _buckets.Clear(); _count = 0; _sum = 0; }
    }
}

/// <summary>直方图快照（不可变）。</summary>
public sealed record HistogramSnapshot(IReadOnlyDictionary<double, long> Buckets, long Count, double Sum);
