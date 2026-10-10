namespace Modules.Node.Relations;

/// <summary>声明关系端点（Source / Target）：资源类型 + Id + 可选端口。</summary>
public sealed class RelationEndpoint
{
    public string ResourceType { get; init; } = NodeResourceTypes.Node;

    public string Id { get; init; } = string.Empty;

    /// <summary>端口（node_to_node 数据流边用；keymapping_to_node 可不填）。</summary>
    public string? Port { get; init; }
}

/// <summary>
/// 主动声明的关系记录（G3 声明式构建，权威在 SQL ur_nodes，NodeType=relation）。
/// 持久化为节点记录的 PayloadJson；上层守卫据此推导边并写入图（声明优先，推导不覆盖声明）。
/// </summary>
public sealed class RelationDeclaration
{
    public RelationEndpoint Source { get; init; } = new();

    public RelationEndpoint Target { get; init; } = new();

    /// <summary>关系类型：node_to_node | keymapping_to_node | 自定义。默认 = <see cref="ReferenceTypes.NodeToNode"/>（A1：唯一字面量源）。</summary>
    public string RefType { get; init; } = ReferenceTypes.NodeToNode;

    /// <summary>关联的 KeyMapping 存储键（keymapping_to_node 时填充；指向节点库承载的分类键）。</summary>
    public string? KeyMappingRef { get; init; }

    /// <summary>状态：active（生效）| revoked（撤销，级联清边）。</summary>
    public string Status { get; init; } = "active";

    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}
