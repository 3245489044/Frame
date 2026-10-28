using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Topology;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.Readers;

/// <summary>
/// **领域端口**层次读取器 —— 只读已建模的包含边：
/// <c>history_owns_task</c> → <see cref="IContainerMemberPort.GetMemberKeysAsync"/>、
/// <c>cross_db_link</c> → <see cref="IContainerMemberPort.GetMemberNodeKeysAsync"/>。
/// <para>其余包含边（如自递归 <c>task_owns_task</c>）返回空集，由组合器回退引用图读取器。</para>
/// <para>不实现反向（子 → 父）查询：领域端口无入边能力，反向由引用图读取器提供。</para>
/// </summary>
internal sealed class HierarchyDomainPortReader : IHierarchyRelationReader
{
    private readonly IContainerMemberPort? _reference;
    private readonly string _workflowId;

    /// <param name="reference">三层层级引用管理端口（可空；未装配 → 子集空）。</param>
    /// <param name="workflowId">节点层工作流命名空间（已归一化）。</param>
    public HierarchyDomainPortReader(IContainerMemberPort? reference, string workflowId)
    {
        _reference = reference;
        _workflowId = workflowId;
    }

    /// <summary>是否已装配引用端口（供组合器诊断）。</summary>
    public bool IsAttached => _reference is not null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (_reference is null || parent.IsEmpty) return Array.Empty<LevelRef>();

        ct.ThrowIfCancellationRequested();

        if (HierarchyLevelRefs.IsHistory(parent) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.HistoryOwnsTask, StringComparison.Ordinal))
        {
            var taskKeys = await _reference.GetMemberKeysAsync(parent.Id, ct).ConfigureAwait(false);
            return Map(taskKeys, HierarchyLevelRefs.Task);
        }

        if (HierarchyLevelRefs.IsTask(parent) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.CrossDbLink, StringComparison.Ordinal))
        {
            var nodeKeys = await _reference.GetMemberNodeKeysAsync(parent.Id, _workflowId, ct).ConfigureAwait(false);
            return Map(nodeKeys, key => HierarchyLevelRefs.Node(_workflowId, key));
        }

        return Array.Empty<LevelRef>();
    }

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, HierarchyContainment containment, CancellationToken ct = default)
        => Task.FromResult<LevelRef?>(null);

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, string parentLevelId, HierarchyContainment containment, CancellationToken ct = default)
        => Task.FromResult<LevelRef?>(null);

    /// <summary>键集 → 层引用集（空白键先过滤，再交内核归一化保序去重 / 滤空）。</summary>
    private static IReadOnlyList<LevelRef> Map(IReadOnlyList<string>? keys, Func<string, LevelRef> project)
    {
        if (keys is not { Count: > 0 }) return Array.Empty<LevelRef>();

        var projected = new List<LevelRef>(keys.Count);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            projected.Add(project(key));
        }
        return LevelRefSet.Normalize(projected);
    }
}
