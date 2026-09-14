using Strategy.Observability.Envelope;

namespace Strategy.Observability.Persistence;

/// <summary>
/// 持久化行模型（E5）：= 事件信封 + 作用域行键(scopeId/eventSeq) + 关联键快照。
/// 关联键来自 E4 ambient 捕获，作为行字段持久化而非进入信封本体。
/// </summary>
public sealed class PersistedEvent
{
    public required string ScopeId { get; init; }

    public required long EventSeq { get; init; }

    public required ObservabilityEvent Event { get; init; }

    public string? TraceId { get; init; }

    public string? SessionId { get; init; }

    public string? CorrelationScopeKey { get; init; }
    /// <summary>运行标识（G4 关联维度补全；由 E4 ambient 捕获）。</summary>
    public string? RunId { get; init; }
    /// <summary>轮次标识（G4 关联维度补全；由 E4 ambient 捕获）。</summary>
    public string? RoundId { get; init; }
}
