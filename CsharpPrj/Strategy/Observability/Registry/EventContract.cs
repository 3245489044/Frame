namespace Strategy.Observability.Registry;

/// <summary>
/// 事件契约（E3）：声明式描述一种 EventKind。内核不做 switch(EventKind) 语义判断，
/// 新增观测点 = 注册契约；契约即版本（Version 随契约演进递增）。
/// </summary>
public sealed class EventContract
{
    public required string EventKind { get; init; }

    /// <summary>该事件归属的实体类型名（为空 = 不限）。</summary>
    public string? EntityType { get; init; }

    /// <summary>载荷(JSON)必填字段名。</summary>
    public IReadOnlyList<string> Required { get; init; } = Array.Empty<string>();

    /// <summary>声明本事件应携带的度量键（值必须为数值）。</summary>
    public IReadOnlyList<string> MetricKeys { get; init; } = Array.Empty<string>();

    /// <summary>载荷字段的枚举约束：字段名 → 允许值集合。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Enums { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    public int Version { get; init; } = 1;
}
