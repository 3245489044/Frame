namespace Operation.HierarchyMethod.Reference;

/// <summary>三层实体类型（history / task / node）—— 执行标识符 <see cref="EntityRef"/> 的第一维。</summary>
public enum EntityKind
{
    /// <summary>上层容器（History = Task 更上层的 class 容器，归拢多 task 多 node）。</summary>
    History,

    /// <summary>任务（AgentTaskItem / ur_tasks）。</summary>
    Task,

    /// <summary>产出节点（ur_nodes，workflowId 命名空间下寻址）。</summary>
    Node,
}
