using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 层级/批量作用域（设计 §6.2）：task 选指定 nodes / 全部 nodes；history 切指定 tasks（及 nodes）/ 全部 tasks（及 nodes）。
/// 沿引用边向下游批量写 exec.flag（history→tasks→nodes 逐层收窄，父层标志默认作用于子层，子层可覆盖）。
/// </summary>
public enum FlagScopeKind
{
    /// <summary>task 作用域：改指定/全部 nodes 标志（Targets = nodeKey[]，可空 = 全部 nodes）。</summary>
    TaskNodes,

    /// <summary>history 作用域：切指定/全部 tasks（及递归其 nodes）标志（Targets = taskKey[]，可空 = 全部 tasks）。</summary>
    HistoryTasks,
}

/// <summary>
/// 标识符层级/批量作用域声明。
/// </summary>
/// <param name="Kind">作用域类型。</param>
/// <param name="OwnerId">所有者 Id（TaskNodes → taskKey；HistoryTasks → historyId）。</param>
/// <param name="WorkflowId">工作流命名空间（仅 TaskNodes 显式 nodeKey 需要；缺省 agent-round）。</param>
/// <param name="Targets">目标键集（可空 = 该作用域全部目标：task 全部 nodes / history 全部 tasks）。</param>
public sealed record FlagScope(
    FlagScopeKind Kind,
    string OwnerId,
    string? WorkflowId = null,
    IReadOnlyCollection<string>? Targets = null)
{
    /// <summary>task 选指定 nodes 改标志。</summary>
    public static FlagScope TaskSelectedNodes(string taskKey, string workflowId, IReadOnlyCollection<string> nodeKeys)
        => new(FlagScopeKind.TaskNodes, taskKey, workflowId, nodeKeys);

    /// <summary>task 改全部 nodes 标志。</summary>
    public static FlagScope TaskAllNodes(string taskKey)
        => new(FlagScopeKind.TaskNodes, taskKey, null, null);

    /// <summary>history 切指定 tasks（及其 nodes）。</summary>
    public static FlagScope HistorySelectedTasks(string historyId, IReadOnlyCollection<string> taskKeys)
        => new(FlagScopeKind.HistoryTasks, historyId, null, taskKeys);

    /// <summary>history 切全部 tasks / nodes。</summary>
    public static FlagScope HistoryAllTasks(string historyId)
        => new(FlagScopeKind.HistoryTasks, historyId, null, null);
}
