using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Topology;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.Readers;

/// <summary>
/// **引用图**层次读取器 —— 把引用图出/入边翻译为父/子事实（通用包含边，含自递归 <c>task_owns_task</c>）。
/// <para>只读、不写边（写边唯一 owner = DataStoreDriven.ReferenceGraphCoordinator）；未装配 → 空集 / null。</para>
/// <para>端点投影统一走 <see cref="HierarchyEndpointProjection"/>（与领域端口读取器共用）。</para>
/// </summary>
internal sealed class ReferenceGraphHierarchyReader : IHierarchyRelationReader
{
    private readonly IResourceReferenceStore? _referenceStore;
    private readonly string _workflowId;

    /// <param name="referenceStore">引用图读写端口（可空；未装配 → 空集 / null）。</param>
    /// <param name="workflowId">节点层工作流命名空间（已归一化）。</param>
    public ReferenceGraphHierarchyReader(IResourceReferenceStore? referenceStore, string workflowId)
    {
        _referenceStore = referenceStore;
        _workflowId = workflowId;
    }

    /// <summary>是否已装配引用图端口。</summary>
    public bool IsAttached => _referenceStore is not null;

    /// <summary>是否支持反向（子 → 父）查询（须已装配引用图端口）。</summary>
    public bool SupportsReverse => _referenceStore is not null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (_referenceStore is null || parent.IsEmpty) return Array.Empty<LevelRef>();

        var parentResource = HierarchyEndpointProjection.ParentResourceOf(parent, _workflowId);
        if (parentResource is null) return Array.Empty<LevelRef>();

        var references = await _referenceStore.GetReferencesAsync(parentResource, includeIncoming: false, ct)
            .ConfigureAwait(false);
        var results = new List<LevelRef>();
        foreach (var reference in references)
        {
            if (reference is null) continue;
            if (!string.Equals(reference.RefType, containment.ContainsEdgeType, StringComparison.Ordinal)) continue;
            if (!HierarchyEndpointProjection.IsSameResource(reference.Source, parentResource)) continue;
            if (HierarchyEndpointProjection.ProjectChild(containment.ChildLevelId, reference.Target, _workflowId) is not { } child) continue;
            results.Add(child);
        }
        return LevelRefSet.Normalize(results);
    }

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, HierarchyContainment containment, CancellationToken ct = default)
        => ResolveParentAsync(child, containment, parentLevelId: null, ct);

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, string parentLevelId, HierarchyContainment containment, CancellationToken ct = default)
        => ResolveParentAsync(child, containment, parentLevelId, ct);

    /// <summary>
    /// 反向解析父节点：按包含边类型确定子端资源与期望父端层，读引用图**入边**并投影回层引用。
    /// 任一步不匹配即返回 null（未装配读端口 / 边类型不符 / 端点类型不符 / Id 约定不匹配）。
    /// </summary>
    private async Task<LevelRef?> ResolveParentAsync(
        LevelRef child, HierarchyContainment containment, string? parentLevelId, CancellationToken ct)
    {
        if (_referenceStore is null || child.IsEmpty || containment is null) return null;

        var childResource = HierarchyEndpointProjection.ChildResourceOf(child, containment, _workflowId);
        if (childResource is null) return null;

        var references = await _referenceStore.GetReferencesAsync(childResource, includeIncoming: true, ct)
            .ConfigureAwait(false);
        foreach (var reference in references)
        {
            if (reference is null) continue;
            if (!string.Equals(reference.RefType, containment.ContainsEdgeType, StringComparison.Ordinal)) continue;
            // 以端点相等判定入边（不依赖 IsIncoming 标志，兼容不同读实现）。
            if (!HierarchyEndpointProjection.IsSameResource(reference.Target, childResource)) continue;
            if (HierarchyEndpointProjection.ProjectParent(child, reference.Source, parentLevelId) is { } parent) return parent;
        }
        return null;
    }
}
