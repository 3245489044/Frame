using Strategy.Observability.Ports;

namespace Strategy.Observability.Projection;

/// <summary>
/// 观测投影门面（E13）：通用驱动层的<b>四根取值口</b>——<c>Port(...)</c> / <c>Event(...)</c> /
/// <c>Timeline(...)</c> / <c>Aggregate(...)</c>。
/// 只提供取值根，不引入新断言算子（算子面由外层封顶）。
/// </summary>
public static class ObservationProjection
{
    /// <summary>端口根：某端口的调用记录投影。</summary>
    public static PortProjection Port(string portKey, CallLog log)
    {
        PortGuard.Required(log, nameof(log));
        return new PortProjection(portKey, log.For(portKey));
    }

    /// <summary>时间线根：按运行标识聚合事件序。</summary>
    public static TimelineProjection Timeline(string runId, IEnumerable<ObservationOutcome> events)
        => new(runId, events);

    /// <summary>聚合根：单度量键的数值聚合。</summary>
    public static AggregateProjection Aggregate(string metricKey, IEnumerable<ObservationOutcome> source)
        => AggregateProjection.Over(metricKey, source);

    /// <summary>事件根：取首个匹配分类的观测产物（无 → null）。</summary>
    public static ObservationOutcome? Event(string kind, IEnumerable<ObservationOutcome> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        PortGuard.Required(source, nameof(source));
        return source.FirstOrDefault(e => string.Equals(e.Kind, kind, StringComparison.Ordinal));
    }

    /// <summary>事件族根：取全部匹配分类的观测产物（事件序）。</summary>
    public static IReadOnlyList<ObservationOutcome> Events(string kind, IEnumerable<ObservationOutcome> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        PortGuard.Required(source, nameof(source));
        return source.Where(e => string.Equals(e.Kind, kind, StringComparison.Ordinal)).ToArray();
    }
}
