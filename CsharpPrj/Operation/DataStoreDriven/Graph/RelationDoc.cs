namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 关系声明文档载荷（G1 / 设计 §4）：<c>ur_relations</c> 文档 = <c>{ _id, Doc }</c>，<c>Doc</c> 为 USharp <c>[Record]</c> 文本真源，
/// 本记录为编解码目标（经 <c>IDocumentCodec</c>）。<c>_id</c> = SQL NodeKey 文法（<c>relations.{workflowId}.{relationId}</c>），
/// 不新造键空间（键构造只经 <c>RelationDeclarationKeys.NodeKey</c> / <c>NodeKeyPrefix.BuildRelation</c> 单源）。
/// 关系声明权威归属 = LiteDB（设计 §2：LiteDB 为唯一逻辑关系持有库）。撤销 = 置 <c>status=revoked</c>（软删，只增）。
/// </summary>
public sealed record RelationDoc
{
    /// <summary>声明 Id（workflow 内唯一，幂等 upsert 依据）。</summary>
    public string RelationId { get; init; } = string.Empty;

    /// <summary>声明所属工作流 Id。</summary>
    public string WorkflowId { get; init; } = string.Empty;

    /// <summary>声明节点规范键（= _id；属键名对应契约，1:1 可追溯）。</summary>
    public string NodeKey { get; init; } = string.Empty;

    /// <summary>源端资源 Id。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>源端资源类型。</summary>
    public string SourceType { get; init; } = string.Empty;

    /// <summary>源端端口（可空）。</summary>
    public string? SourcePort { get; init; }

    /// <summary>目标端资源 Id。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>目标端资源类型。</summary>
    public string TargetType { get; init; } = string.Empty;

    /// <summary>目标端端口（可空）。</summary>
    public string? TargetPort { get; init; }

    /// <summary>关系类型（node_to_node / keymapping_to_node / 自定义）。</summary>
    public string RefType { get; init; } = string.Empty;

    /// <summary>关联 KeyMapping 存储键（可空）。</summary>
    public string? KeyMappingRef { get; init; }

    /// <summary>声明状态：active（生效）| revoked（撤销，软删）。</summary>
    public string Status { get; init; } = "active";

    /// <summary>是否生效声明。</summary>
    public bool IsActive => string.Equals(Status, "active", System.StringComparison.OrdinalIgnoreCase);
}
