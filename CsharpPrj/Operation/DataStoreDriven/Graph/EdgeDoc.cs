namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用边文档载荷（G1 / D2）：引用库边文档 = <c>{ _id, Doc }</c>，<c>Doc</c> 为 USharp <c>[Record]</c> 文本真源，
/// 本记录为编解码目标（经 <c>IDocumentCodec</c>）。取代原 BSON 顶层字段（SourceId/TargetId/RefType/Strength）。
/// 关系权威归属 = 引用库（D3）；节点库不再承载关系。
/// </summary>
public sealed record EdgeDoc
{
    /// <summary>源端点键（ResourceIdentifier.GetUniqueKey）。</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>源端点资源类型（可空）。</summary>
    public string? SourceType { get; init; }

    /// <summary>目标端点键。</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>目标端点资源类型（可空）。</summary>
    public string? TargetType { get; init; }

    /// <summary>边类型（词表边名 / 节点关系编码 refType）。</summary>
    public string RefType { get; init; } = string.Empty;

    /// <summary>边强度。</summary>
    public double Strength { get; init; } = 1.0;
}
