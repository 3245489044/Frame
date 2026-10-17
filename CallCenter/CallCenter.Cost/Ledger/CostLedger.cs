using CallCenter.Cost.Costing;
using CallCenter.Cost.Metering;

namespace CallCenter.Cost.Ledger;

/// <summary>成本写入端口（由宿主实现；本层不落库）。</summary>
public interface ICostSink
{
    ValueTask WriteAsync(IReadOnlyList<CostSpan> spans, CancellationToken ct = default);
}

/// <summary>落库聚合结果（任一 Unknown ⇒ 聚合 Unknown，Amount 为 null）。</summary>
public sealed record CostAggregate(
    decimal? Amount,
    string Currency,
    CostSource Source,
    int SpanCount,
    IReadOnlyList<CostBreakdownItem> Breakdown);

/// <summary>
/// 内存成本账本：按 InstanceRef 归集；幂等键 = TraceId + SpanId（重复结算不叠加）。
/// </summary>
public sealed class CostLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CostSpan> _byKey = new(StringComparer.Ordinal);

    /// <summary>加入跨度；重复幂等键 ⇒ false（不叠加）。</summary>
    public bool Add(CostSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        lock (_gate)
        {
            return _byKey.TryAdd(span.IdempotencyKey, span);
        }
    }

    public int AddRange(IEnumerable<CostSpan> spans)
    {
        var added = 0;
        foreach (var span in spans)
        {
            if (Add(span))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>按实例聚合。</summary>
    public CostAggregate Aggregate(InstanceRef instance)
    {
        List<CostSpan> spans;
        lock (_gate)
        {
            spans = _byKey.Values
                .Where(s => string.Equals(s.Instance.Kind, instance.Kind, StringComparison.Ordinal)
                    && string.Equals(s.Instance.Id, instance.Id, StringComparison.Ordinal))
                .ToList();
        }

        return AggregateCore(spans);
    }

    /// <summary>全量聚合。</summary>
    public CostAggregate AggregateAll()
    {
        lock (_gate)
        {
            return AggregateCore(_byKey.Values.ToList());
        }
    }

    public IReadOnlyList<CostSpan> Snapshot()
    {
        lock (_gate)
        {
            return _byKey.Values.ToList();
        }
    }

    private static CostAggregate AggregateCore(IReadOnlyList<CostSpan> spans)
    {
        if (spans.Count == 0)
        {
            return new CostAggregate(null, "USD", CostSource.Unknown, 0, Array.Empty<CostBreakdownItem>());
        }

        var currency = spans.Select(s => s.Cost?.Currency).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "USD";
        if (spans.Any(s => s.Cost is null || !s.Cost.IsKnown))
        {
            return new CostAggregate(null, currency, CostSource.Unknown, spans.Count, Array.Empty<CostBreakdownItem>());
        }

        var items = spans
            .SelectMany(s => s.Cost!.Breakdown)
            .GroupBy(i => i.Item, StringComparer.Ordinal)
            .Select(g => new CostBreakdownItem(g.Key, g.Sum(i => i.Amount)))
            .ToList();
        var amount = items.Sum(i => i.Amount);
        var source = spans.All(s => s.Cost!.Source == CostSource.Measured)
            ? CostSource.Measured
            : CostSource.Estimated;
        return new CostAggregate(amount, currency, source, spans.Count, items);
    }
}
