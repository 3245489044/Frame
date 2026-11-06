using System.Globalization;
using System.Runtime.CompilerServices;
using Strategy.Observability.Entity;
using Strategy.Observability.Envelope;
using Strategy.Observability.Persistence;
using Strategy.Observability.Ports;

namespace Strategy.Observability.Query;

/// <summary>查询投影端口（E6）：三个可组合原语，不提供领域查询；领域投影由应用层拼装。</summary>
public interface IObservabilityQuery
{
    IAsyncEnumerable<ObservabilityEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// 时序行（含行键关联维度 RunId / RoundId）。需要关联键的投影（如 <c>Timeline</c> 的 Rounds）必须走本口，
    /// <see cref="TimelineAsync"/> 只返回信封本体、会丢失行字段。
    /// </summary>
    IAsyncEnumerable<PersistedEvent> TimelineRecordsAsync(string scopeId, long? afterSeq = null, int limit = 100, CancellationToken ct = default);

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
        await foreach (var p in TimelineRecordsAsync(scopeId, afterSeq, limit, ct).ConfigureAwait(false))
            yield return p.Event;
    }

    public async IAsyncEnumerable<PersistedEvent> TimelineRecordsAsync(string scopeId, long? afterSeq = null, int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var p in _store.TimelineAsync(scopeId, afterSeq, limit, ct).ConfigureAwait(false))
            yield return p;
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
        var samples = keySet.ToDictionary(k => k, _ => new List<double>(), StringComparer.Ordinal);

        await foreach (var p in _store.TimelineAsync(scopeId, null, int.MaxValue, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!kindSet.Contains(p.Event.EventKind)) continue;
            if (fromUtc.HasValue && !AtOrAfterUtc(p.Event.CreatedAt, fromUtc.Value)) continue;
            if (p.Event.Metrics is null) continue;
            foreach (var key in keySet)
            {
                if (p.Event.Metrics.TryGetValue(key, out var v)) samples[key].Add(v);
            }
        }

        // 累加口径单源 = PortValues.NumericStats（与 AggregateProjection / MetricTotalsProjection 同一实现）。
        var result = new List<MetricAggregate>();
        foreach (var key in keySet)
        {
            var stats = PortValues.NumericStats.Over(samples[key]);
            result.Add(stats.Count == 0
                ? new MetricAggregate(key, 0, 0, 0, null, null)
                : new MetricAggregate(key, stats.Count, stats.Sum, stats.Mean, stats.Max, stats.Min));
        }
        return result;
    }

    /// <summary>事件创建时刻（"O" 往返格式）是否不早于 <paramref name="fromUtc"/>；解析失败按不满足处理。</summary>
    private static bool AtOrAfterUtc(string createdAt, DateTime fromUtc)
        => DateTime.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
           && dt >= fromUtc;
}
