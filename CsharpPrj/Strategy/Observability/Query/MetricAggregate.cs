namespace Strategy.Observability.Query;

/// <summary>单度量键的聚合结果（E6/E7）。</summary>
public sealed record MetricAggregate(
    string MetricKey,
    long Count,
    double Sum,
    double Average,
    double? Max,
    double? Min);
