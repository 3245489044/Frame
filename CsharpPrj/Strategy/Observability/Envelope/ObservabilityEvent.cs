using Strategy.Observability.Entity;

namespace Strategy.Observability.Envelope;

/// <summary>
/// 事件信封（E2）：字段全通用，**不出现任何应用/Agent 字段**。
/// token / elapsed / score 等一律入 <see cref="Metrics"/> 字典；关联键(TraceId/SessionId/ScopeKey)
/// 不进信封本身，由 E4 从 ambient 作用域自动捕获并以行键另行持久化。
/// </summary>
public sealed class ObservabilityEvent
{
    public required EntityId Entity { get; init; }

    /// <summary>ScopeId 内单调递增序号。</summary>
    public required long EventSeq { get; init; }

    /// <summary>注册过的分类名（见 E3 契约注册表）。</summary>
    public required string EventKind { get; init; }

    public string? Phase { get; init; }

    public string? Stage { get; init; }

    public IReadOnlyDictionary<string, double>? Metrics { get; init; }

    public string? PayloadJson { get; init; }

    public required string CreatedAt { get; init; }

    public int Version { get; init; } = 1;
}
