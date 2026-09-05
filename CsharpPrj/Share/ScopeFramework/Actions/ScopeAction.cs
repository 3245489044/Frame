namespace Share.ScopeFramework;

/// <summary>
/// 强类型既有 action 描述（取代手拼 JSON 字符串）。字段即既有 RunRequest 契约的语义子集；
/// 仅非 null 字段会被序列化输出，从而保持"字段含义不变"红线（不新增原产物中不存在的字段）。
/// 序列化由唯一序列化器 <see cref="ScopeActionSerializer"/> 负责，Preview 与执行共用同一描述。
/// </summary>
public sealed record ScopeAction
{
    /// <summary>action 判别：runLoop|task|node|addResource|removeResource|addReference|removeReference|query|...</summary>
    public required string Action { get; init; }

    /// <summary>query 目标（如 nodesByTask）。</summary>
    public string? Target { get; init; }

    public string? Op { get; init; }
    public string? NodeKey { get; init; }
    public string? NodeType { get; init; }
    public string? ThreadId { get; init; }
    public object? Payload { get; init; }
    public string? TaskKey { get; init; }
    public string? Input { get; init; }
    public string? Mode { get; init; }

    /// <summary>激活标志。可空：null 不输出，保持原产物字段集合（入队默认激活语义由执行端兜底）。</summary>
    public bool? Active { get; init; }

    public int? Priority { get; init; }
    public int? MaxRetries { get; init; }
    public string? ExecutorId { get; init; }
    public IReadOnlyList<string>? RelatedNodes { get; init; }
    public string? Key { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? RefType { get; init; }
    public string? ResourceType { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }

    /// <summary>直通原始 JSON：非 null 时序列化器原样输出、不参与字段序列化（S0 直通语义，保文本兼容）。</summary>
    public string? RawJson { get; init; }
}
