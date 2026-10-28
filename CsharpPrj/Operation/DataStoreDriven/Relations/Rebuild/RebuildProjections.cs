using System;
using System.Collections.Generic;
using UniversalWorkflowFramework.Relations;

namespace Operation.DataStoreDriven.Relations.Rebuild;

/// <summary>task 实体的重建行（SQL 列 → 中性 DTO 的映射由 Source 承担）。</summary>
public sealed record TaskRebuildRow(
    string TaskKey,
    string? HistoryId = null,
    string? ParentTaskKey = null,
    IReadOnlyList<string>? RelatedNodes = null,
    string? WorkflowId = null);

/// <summary>会话实体的重建行（会话消息/产出节点）。</summary>
public sealed record SessionRebuildRow(
    string SessionName,
    IReadOnlyList<string> NodeKeys,
    string? WorkflowId = null);

/// <summary><c>ur_tasks.HistoryId</c> 非空 → <c>mount_task_history</c>（history → task）。</summary>
public sealed class TaskContainerProjection : IRebuildProjection<TaskRebuildRow>
{
    public IEnumerable<RebuildAction> Project(TaskRebuildRow item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.HistoryId) || string.IsNullOrWhiteSpace(item.TaskKey))
            yield break;

        yield return new RebuildAction(
            RelationOperationIds.MountTaskHistory,
            new RelationRequest(item.HistoryId, TargetKey: item.TaskKey));
    }
}

/// <summary><c>ur_tasks.ParentTaskKey</c> 非空 → <c>mount_subtask</c>（parent → child）。</summary>
public sealed class SubTaskProjection : IRebuildProjection<TaskRebuildRow>
{
    public IEnumerable<RebuildAction> Project(TaskRebuildRow item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.ParentTaskKey) || string.IsNullOrWhiteSpace(item.TaskKey))
            yield break;
        if (string.Equals(item.ParentTaskKey, item.TaskKey, StringComparison.Ordinal))
            yield break;

        yield return new RebuildAction(
            RelationOperationIds.MountSubTask,
            new RelationRequest(item.ParentTaskKey, TargetKey: item.TaskKey));
    }
}

/// <summary>task 关联节点 → <c>mount_node_task</c>（批量 <c>TargetKeys</c>）。</summary>
public sealed class TaskNodeProjection : IRebuildProjection<TaskRebuildRow>
{
    public IEnumerable<RebuildAction> Project(TaskRebuildRow item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.TaskKey) || item.RelatedNodes is not { Count: > 0 })
            yield break;

        yield return new RebuildAction(
            RelationOperationIds.MountNodeTask,
            new RelationRequest(
                item.TaskKey,
                TargetKeys: item.RelatedNodes,
                Namespace: string.IsNullOrWhiteSpace(item.WorkflowId) ? null : item.WorkflowId));
    }
}

/// <summary>会话消息/产出节点 → <c>mount_session_node</c>。</summary>
public sealed class SessionNodeProjection : IRebuildProjection<SessionRebuildRow>
{
    public IEnumerable<RebuildAction> Project(SessionRebuildRow item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.SessionName) || item.NodeKeys is not { Count: > 0 })
            yield break;

        yield return new RebuildAction(
            RelationOperationIds.MountSessionNode,
            new RelationRequest(
                item.SessionName,
                TargetKeys: item.NodeKeys,
                Namespace: string.IsNullOrWhiteSpace(item.WorkflowId) ? null : item.WorkflowId));
    }
}
