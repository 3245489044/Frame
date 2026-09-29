using System;

namespace Operation.DataStoreDriven.Abstractions
{
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
    /// 持久化到 <see cref="NodeRecord.PayloadJson"/>；守卫据此推导边并写入图（声明优先，推导不覆盖声明）。
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

    /// <summary>声明入口：relationId（声明节点唯一键，幂等 upsert 依据）+ 声明内容。</summary>
    public sealed class RelationDeclarationEntry
    {
        public string RelationId { get; init; } = string.Empty;

        public RelationDeclaration Declaration { get; init; } = new();
    }

    /// <summary>声明关系节点的规范键 / Id 构造（NodeKey="relations.{workflowId}.{relationId}"，幂等 upsert 依据）。</summary>
    public static class RelationDeclarationKeys
    {
        /// <summary>声明节点规范键：{workflowId}.{relationId}（对齐 <see cref="KeyMappingKeys.NodeLevelKey"/> 的节点级规范键命名）。</summary>
        public static string RelationId(string workflowId, string relationId) => KeyMappingKeys.NodeLevelKey(workflowId, relationId);

        /// <summary>声明节点 NodeKey（ur_nodes 唯一键）：relations.{workflowId}.{relationId}。</summary>
        public static string NodeKey(string workflowId, string relationId) => NodeKeyPrefix.BuildRelation(workflowId, relationId);
    }
}
