using System;
using Modules.DeclarativeDataflow.Model;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.Readers;

/// <summary>
/// 引用图端点 ↔ <see cref="LevelRef"/> 的投影（**唯一实现**）—— 领域端口读取器与引用图读取器共用，
/// 消除"节点/任务/历史资源标识"与"层引用"两套拼装。
/// <para>全部为纯函数；未匹配层 / 资源类型不符 → <c>null</c>（不猜）。</para>
/// </summary>
internal static class HierarchyEndpointProjection
{
    /// <summary>父端资源标识（按父层判定；未知层返回 null）。</summary>
    public static ResourceIdentifier? ParentResourceOf(LevelRef parent, string defaultWorkflowId)
    {
        if (HierarchyLevelRefs.IsHistory(parent))
            return new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HierarchyResourceKeys.History(parent.Id) };

        if (HierarchyLevelRefs.IsTask(parent))
            return new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = parent.Id };

        if (HierarchyLevelRefs.IsNode(parent))
            return NodeResourceOf(parent, defaultWorkflowId);

        return null;
    }

    /// <summary>节点端资源标识（Id = {workflowId}.{nodeKey}，Location = workflowId）；正反向共用同一构造。</summary>
    public static ResourceIdentifier NodeResourceOf(LevelRef node, string defaultWorkflowId)
    {
        var workflowId = WorkflowOf(node, defaultWorkflowId);
        return new ResourceIdentifier
        {
            ResourceType = NodeResourceTypes.Node,
            Id = HierarchyResourceKeys.Node(workflowId, node.Id),
            Location = workflowId,
        };
    }

    /// <summary>子端资源标识（节点层带 workflowId 命名空间；任务层即裸 taskKey）；边类型不符返回 null。</summary>
    public static ResourceIdentifier? ChildResourceOf(
        LevelRef child, HierarchyContainment containment, string defaultWorkflowId)
    {
        if (HierarchyLevelRefs.IsNode(child) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.CrossDbLink, StringComparison.Ordinal))
            return NodeResourceOf(child, defaultWorkflowId);

        if (HierarchyLevelRefs.IsTask(child) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.HistoryOwnsTask, StringComparison.Ordinal))
            return new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = child.Id };

        return null;
    }

    /// <summary>子端资源 → 层引用（层判定由包含声明的 ChildLevelId 给出，资源类型须一致）。</summary>
    public static LevelRef? ProjectChild(string childLevelId, ResourceIdentifier? target, string defaultWorkflowId)
    {
        if (target is null) return null;

        if (string.Equals(childLevelId, HierarchySchemas.TaskLevel, StringComparison.Ordinal) &&
            string.Equals(target.ResourceType, NodeResourceTypes.Task, StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(target.Id) ? null : HierarchyLevelRefs.Task(target.Id!);

        if (string.Equals(childLevelId, HierarchySchemas.NodeLevel, StringComparison.Ordinal) &&
            string.Equals(target.ResourceType, NodeResourceTypes.Node, StringComparison.Ordinal))
            return HierarchyResourceKeys.TryParseNodeKey(target.Id, defaultWorkflowId, out var nodeKey)
                ? HierarchyLevelRefs.Node(defaultWorkflowId, nodeKey)
                : null;

        if (string.Equals(childLevelId, HierarchySchemas.HistoryLevel, StringComparison.Ordinal) &&
            string.Equals(target.ResourceType, NodeResourceTypes.History, StringComparison.Ordinal))
            return HierarchyResourceKeys.TryParseHistoryId(target.Id, out var historyId)
                ? HierarchyLevelRefs.History(historyId)
                : null;

        return null;
    }

    /// <summary>父端资源 → 层引用（层判定与 <paramref name="parentLevelId"/> 一致才接受）。</summary>
    public static LevelRef? ProjectParent(LevelRef child, ResourceIdentifier? source, string? parentLevelId)
    {
        if (source is null) return null;

        if (HierarchyLevelRefs.IsNode(child))
        {
            if (!MatchLevel(parentLevelId, HierarchySchemas.TaskLevel)) return null;
            if (!string.Equals(source.ResourceType, NodeResourceTypes.Task, StringComparison.Ordinal)) return null;
            return string.IsNullOrWhiteSpace(source.Id) ? null : HierarchyLevelRefs.Task(source.Id!);
        }

        if (!MatchLevel(parentLevelId, HierarchySchemas.HistoryLevel)) return null;
        if (!string.Equals(source.ResourceType, NodeResourceTypes.History, StringComparison.Ordinal)) return null;
        return HierarchyResourceKeys.TryParseHistoryId(source.Id, out var historyId)
            ? HierarchyLevelRefs.History(historyId)
            : null;
    }

    /// <summary>层 Id 与期望父层一致（<paramref name="parentLevelId"/> 为 null = 不消歧）。</summary>
    public static bool MatchLevel(string? parentLevelId, string expectedLevelId)
        => parentLevelId is null || string.Equals(parentLevelId, expectedLevelId, StringComparison.Ordinal);

    /// <summary>资源标识同端判定（类型 + Id）。</summary>
    public static bool IsSameResource(ResourceIdentifier? left, ResourceIdentifier right)
        => left is not null
           && string.Equals(left.ResourceType, right.ResourceType, StringComparison.Ordinal)
           && string.Equals(left.Id, right.Id, StringComparison.Ordinal);

    /// <summary>节点层工作流命名空间（引用未带作用域时回退适配器默认值）。</summary>
    public static string WorkflowOf(LevelRef node, string defaultWorkflowId)
        => string.IsNullOrWhiteSpace(node.Scope) ? defaultWorkflowId : node.Scope!;
}
