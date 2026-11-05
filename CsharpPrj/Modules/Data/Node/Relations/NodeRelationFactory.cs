using Modules.Node.Identity;

namespace Modules.Node.Relations;

/// <summary>
/// 节点关系构造工厂 —— 按工作流节点语义构造三类关系的端点资源（资源 Id 以 workflowId 命名空间隔离）。
/// 端点 Id 单源 = <see cref="NodeEndpointIds.Node"/>（图端点 Id 文法 <c>{workflowId}.{nodeKey}</c>，
/// 与 KeyMapping 节点级规范键同一键空间），本类禁第二处拼串。
/// </summary>
public static class NodeRelationFactory
{
    /// <summary>节点资源 Id：{workflowId}.{nodeKey}（同 KeyMapping 节点级规范键，见 <see cref="NodeEndpointIds.NodeGrammar"/>）。</summary>
    public static string NodeId(string workflowId, string nodeKey) => NodeEndpointIds.Node(workflowId, nodeKey);

    /// <summary>KeyMapping 资源 Id：{workflowId}.{key}（节点级规范键，见 <see cref="NodeEndpointIds.NodeGrammar"/>）。</summary>
    public static string KeyMappingId(string workflowId, string key) => NodeEndpointIds.Node(workflowId, key);

    /// <summary>KeyMapping ↔ 节点：keymapping 资源 → 节点资源。</summary>
    public static NodeRelation KeyMappingToNode(string workflowId, string keyMappingKey, string nodeKey)
        => new()
        {
            Type = NodeRelationType.KeyMappingToNode,
            SourceResourceType = NodeResourceTypes.KeyMapping,
            SourceId = KeyMappingId(workflowId, keyMappingKey),
            TargetResourceType = NodeResourceTypes.Node,
            TargetId = NodeId(workflowId, nodeKey),
            KeyMappingRef = NodeEndpointIds.Node(workflowId, keyMappingKey),
        };

    /// <summary>节点 ↔ 节点：上游节点输出端口 → 下游节点输入端口（KeyMapping 驱动的数据流组合边）。</summary>
    public static NodeRelation NodeToNode(string workflowId, string fromNode, string fromPort, string toNode, string toPort, string? keyMappingRef = null)
        => new()
        {
            Type = NodeRelationType.NodeToNode,
            SourceResourceType = NodeResourceTypes.Node,
            SourceId = NodeId(workflowId, fromNode),
            SourcePort = fromPort,
            TargetResourceType = NodeResourceTypes.Node,
            TargetId = NodeId(workflowId, toNode),
            TargetPort = toPort,
            KeyMappingRef = keyMappingRef,
        };
}
