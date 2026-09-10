namespace Operation.HierarchyMethod.Nodes;

/// <summary>
/// workflowId 归一化唯一权威 —— 输出并入业务节点后，节点主键为裸 nodeKey，
/// 不再存在 <c>nodes.{workflowId}.</c> 记录键（该键方案已随「模块 A 并入模块 C」废除）。
/// 供 NodeOutputStore / HierarchyLevelRefs / 层处理器等共用，消除多源空值归一化分叉。
/// </summary>
public static class NodeOutputKeys
{
    /// <summary>workflowId 空串归一化为默认值（单一权威）。</summary>
    public static string Normalize(string? workflowId)
        => string.IsNullOrEmpty(workflowId) ? NodeKeys.DefaultWorkflowId : workflowId;
}
