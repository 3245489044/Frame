using Modules.DeclarativeDataflow.Model;
using Operation.HierarchyMethod.Nodes;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow;

/// <summary>
/// 三层实体 ↔ <see cref="LevelRef"/> 的定位映射（**唯一转换点**）：
/// 「历史 Id / 任务键 / (workflowId, nodeKey)」与「层 Id + 实体 Id + 作用域」一一对应。
/// <para>
/// 层判定只看 <see cref="LevelRef.LevelId"/>；<see cref="LevelRef.Scope"/> 仅承载 node 层的
/// workflowId 命名空间（对齐 ur_nodes 寻址），不参与层判定。
/// </para>
/// </summary>
public static class HierarchyLevelRefs
{
    /// <summary>历史容器定位。</summary>
    public static LevelRef History(string historyId) => LevelRef.Of(HierarchySchemas.HistoryLevel, historyId);

    /// <summary>任务定位（taskKey 全局唯一，故无需作用域）。</summary>
    public static LevelRef Task(string taskKey) => LevelRef.Of(HierarchySchemas.TaskLevel, taskKey);

    /// <summary>节点定位（作用域 = 归一化 workflowId，与 ur_nodes 命名空间同源）。</summary>
    public static LevelRef Node(string? workflowId, string nodeKey)
        => LevelRef.Of(HierarchySchemas.NodeLevel, nodeKey, NodeOutputKeys.Normalize(workflowId));

    /// <summary>是否历史层引用。</summary>
    public static bool IsHistory(LevelRef node) => string.Equals(node.LevelId, HierarchySchemas.HistoryLevel, StringComparison.Ordinal);

    /// <summary>是否任务层引用。</summary>
    public static bool IsTask(LevelRef node) => string.Equals(node.LevelId, HierarchySchemas.TaskLevel, StringComparison.Ordinal);

    /// <summary>是否节点层引用。</summary>
    public static bool IsNode(LevelRef node) => string.Equals(node.LevelId, HierarchySchemas.NodeLevel, StringComparison.Ordinal);
}
