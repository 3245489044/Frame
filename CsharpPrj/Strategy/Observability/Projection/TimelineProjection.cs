namespace Strategy.Observability.Projection;

/// <summary>
/// 时间线投影（E13）：按运行标识聚合事件序，并提供轮次切片与分类过滤
/// （<c>Timeline(runId)</c> 取值根），替代测试侧的"分布探针 + 手写切片"。
/// </summary>
public sealed class TimelineProjection
{
    private readonly IReadOnlyList<ObservationOutcome> _events;

    public TimelineProjection(string runId, IEnumerable<ObservationOutcome> events)
    {
        RunId = string.IsNullOrWhiteSpace(runId) ? throw new ArgumentException("运行标识不能为空", nameof(runId)) : runId;
        _events = (events ?? throw new ArgumentNullException(nameof(events))).ToArray();
    }

    /// <summary>运行标识。</summary>
    public string RunId { get; }

    /// <summary>事件序（投影输入序即时间序）。</summary>
    public IReadOnlyList<ObservationOutcome> Events => _events;

    /// <summary>事件条数。</summary>
    public int Count => _events.Count;

    /// <summary>事件分类序（含重复；用于断言"事件出现过 N 次"）。</summary>
    public IReadOnlyList<string> Kinds => _events.Select(e => e.Kind ?? string.Empty).ToArray();

    /// <summary>出现过的轮次标识（出现序，去重）。</summary>
    public IReadOnlyList<string> Rounds
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (var round in _events.Select(e => e.RoundId))
            {
                if (string.IsNullOrEmpty(round) || !seen.Add(round)) continue;
                ordered.Add(round);
            }
            return ordered;
        }
    }

    /// <summary>按分类过滤（Ordinal）。</summary>
    public IReadOnlyList<ObservationOutcome> OfKind(string kind)
        => _events.Where(e => string.Equals(e.Kind, kind, StringComparison.Ordinal)).ToArray();

    /// <summary>按轮次标识切片（Ordinal）。</summary>
    public IReadOnlyList<ObservationOutcome> Round(string roundId)
        => _events.Where(e => string.Equals(e.RoundId, roundId, StringComparison.Ordinal)).ToArray();

    /// <summary>首个匹配分类的事件（无 → null）。</summary>
    public ObservationOutcome? First(string kind)
        => _events.FirstOrDefault(e => string.Equals(e.Kind, kind, StringComparison.Ordinal));

    /// <summary>分类计数（Ordinal 键，出现序）。</summary>
    public IReadOnlyDictionary<string, int> KindCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kind in Kinds)
            counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;
        return counts;
    }
}
