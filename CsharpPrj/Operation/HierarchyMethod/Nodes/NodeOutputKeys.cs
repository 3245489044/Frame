namespace Operation.HierarchyMethod.Nodes;

/// <summary>节点输出存储键方案（ur_nodes NodeType=output 记录）：NodeKey = nodes.{workflowId}.{nodeKey}。
/// 中立承载于 Agent.Contract，供 Core 存储侧与 Agent.Reference 投影侧共用。</summary>
public static class NodeOutputKeys
{
    /// <summary>workflowId 空串归一化为默认值（单一权威，供 Prefix/NodeKey/NodeStore/NodeOutputStore 共用，消除多源归一化分叉）。</summary>
    public static string Normalize(string? workflowId)
        => string.IsNullOrEmpty(workflowId) ? NodeKeys.DefaultWorkflowId : workflowId;

    public static string Prefix(string workflowId)
        => "nodes." + Normalize(workflowId) + ".";

    public static string NodeKey(string workflowId, string nodeKey)
        => Prefix(workflowId) + nodeKey;
}
