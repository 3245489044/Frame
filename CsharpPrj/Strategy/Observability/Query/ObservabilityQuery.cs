using System.Globalization;
using System.Runtime.CompilerServices;
using Strategy.Observability.Entity;
using Strategy.Observability.Envelope;
using Strategy.Observability.Persistence;

namespace Strategy.Observability.Query;

/// <summary>查询投影端口（E6）：三个可组合原语，不提供领域查询；领域投影由应用层拼装。</summary>
public interface IObservabilityQuery
{
    IAsyncEnumerable<ObservabilityEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, CancellationToken ct = default);

    IAsyncEnumerable<ObservabilityEvent> EntityChainAsync(EntityId entity, CancellationToken ct = default);

    Task<IReadOnlyList<MetricAggregate>> AggregateAsync(string scopeId, IEnumerable<string> kinds, IEnumerable<string> metricKeys, DateTime? fromUtc = null, CancellationToken ct = default);
}

/// <summary>投影原语实现（E6）：直接读事件区（事实源），仅做组合不解释语义。</summary>
public sealed class ObservabilityQuery : IObservabilityQuery
{
    private readonly IEventStore _store;

    public ObservabilityQuery(IEventStore store)
    {
        _store = PortGuard.Required(store, nameof(store));
    }

    public async IAsyncEnumerable<ObservabilityEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var p in _store.TimelineAsync(scopeId, afterSeq, limit, ct).ConfigureAwait(false))
            yield return p.Event;
    }

    public async IAsyncEnumerable<ObservabilityEvent> EntityChainAsync(EntityId entity, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var p in _store.EntityChainAsync(entity, ct).ConfigureAwait(false))
            yield return p.Event;
    }

    public async Task<IReadOnlyList<MetricAggregate>> AggregateAsync(string scopeId, IEnumerable<string> kinds, IEnumerable<string> metricKeys, DateTime? fromUtc = null, CancellationToken ct = default)
    {
        var kindSet = new HashSet<string>(PortGuard.Required(kinds, nameof(kinds)), StringComparer.Ordinal);
        var keySet = new HashSet<string>(PortGuard.Required(metricKeys, nameof(metricKeys)), StringComparer.Ordinal);
        var sums = keySet.ToDictionary(k => k, _ => 0.0, StringComparer.Ordinal);
        var counts = keySet.ToDictionary(k => k, _ => 0L, StringComparer.Ordinal);
        var max = keySet.ToDictionary(k => k, _ => double.MinValue, StringComparer.Ordinal);
        var min = keySet.ToDictionary(k => k, _ => double.MaxValue, StringComparer.Ordinal);

        await foreach (var p in _store.TimelineAsync(scopeId, null, int.MaxValue, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!kindSet.Contains(p.Event.EventKind)) continue;
            if (fromUtc.HasValue && !AtOrAfterUtc(p.Event.CreatedAt, fromUtc.Value)) continue;
            if (p.Event.Metrics is null) continue;
            foreach (var key in keySet)
            {
                if (!p.Event.Metrics.TryGetValue(key, out var v)) continue;
                sums[key] += v;
                counts[key]++;
                if (v > max[key]) max[key] = v;
                if (v < min[key]) min[key] = v;
            }
        }

        var result = new List<MetricAggregate>();
        foreach (var key in keySet)
        {
            result.Add(counts[key] == 0
                ? new MetricAggregate(key, 0, 0, 0, null, null)
                : new MetricAggregate(key, counts[key], sums[key], sums[key] / counts[key], max[key], min[key]));
        }
        return result;
    }

    /// <summary>事件创建时刻（"O" 往返格式）是否不早于 <paramref name="fromUtc"/>；解析失败按不满足处理。</summary>
    private static bool AtOrAfterUtc(string createdAt, DateTime fromUtc)
        => DateTime.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
           && dt >= fromUtc;
}
