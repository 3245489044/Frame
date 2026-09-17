namespace DataCenter.Protocol.Standards;

/// <summary>
/// 事件标准项（事件台账 <c>events[x]</c> 的中立形态）：事件类型 / 族（实体）/ <c>carries</c>
/// （携带的字段或度量）/ <c>required</c> / <c>metrics</c> / <c>enum</c>（字段枚举域）/ 作用域 / 版本。
/// </summary>
public sealed record EventStandard(
    string Key,
    string? Entity,
    IReadOnlyList<string> Carries,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Metrics,
    string? Scope,
    int Version,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Enums = null);
