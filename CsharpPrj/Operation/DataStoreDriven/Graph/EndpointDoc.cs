namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 端点文档载荷（G1 / D2）：端点集合文档 = <c>{ _id, Doc }</c>，<c>Doc</c> 为 USharp <c>[Record]</c> 文本真源，
/// 本记录为编解码目标（经 <c>IDocumentCodec</c>）。端点实体真源仍在 SQL，本面仅缓存符号/Metadata 供图可读性。
/// </summary>
public sealed record EndpointDoc
{
    /// <summary>端点资源类型。</summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>端点资源 Id。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>端点符号名（图可读性）。</summary>
    public string? Symbol { get; init; }

    /// <summary>最近更新时刻（UTC ISO-8601）。</summary>
    public string? UpdatedAt { get; init; }

    /// <summary>端点元数据（文本键值；非标量以 InvariantCulture 文本承载）。</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
