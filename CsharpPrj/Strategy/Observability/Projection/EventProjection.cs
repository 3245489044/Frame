namespace Strategy.Observability.Projection;

/// <summary>
/// 事件分类切片投影（<c>Event(&lt;分类&gt;)</c> 取值根）：当前作用域内按事件分类切片，供
/// <c>Count</c> / <c>Observed</c> / <c>Statuses</c> / <c>Rounds</c> / <c>Events[i]</c> 取值断言。
/// <para>
/// 与 <see cref="ObservationProjection.Event"/> 的分工：后者取「首个匹配」单条出证，
/// 本类型取「分类切片视图」（多事件）+ 派生视图，两者语义不同、互不替代。
/// </para>
/// </summary>
public sealed class EventProjection
{
    private readonly IReadOnlyList<ObservationOutcome> _events;

    private EventProjection(string kind, IReadOnlyList<ObservationOutcome> events)
    {
        Kind = kind;
        _events = events;
    }

    /// <summary>事件分类。</summary>
    public string Kind { get; }

    /// <summary>命中条数。</summary>
    public int Count => _events.Count;

    /// <summary>是否已观测到该分类事件。</summary>
    public bool Observed => _events.Count > 0;

    /// <summary>命中事件（时间序）。</summary>
    public IReadOnlyList<ObservationOutcome> Events => _events;

    /// <summary>阶段序（<c>completed</c> / <c>failed</c> …）。</summary>
    public IReadOnlyList<string> Statuses
        => _events.Select(e => e.Status ?? string.Empty).ToArray();

    /// <summary>出现过的轮次标识（出现序，去重）。</summary>
    public IReadOnlyList<string> Rounds
        => _events.Select(e => e.RoundId).Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).ToArray()!;

    /// <summary>按分类从事件序切片。</summary>
    public static EventProjection Of(string kind, IEnumerable<ObservationOutcome> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new EventProjection(
            kind,
            events.Where(e => string.Equals(e.Kind, kind, StringComparison.Ordinal)).ToArray());
    }
}
