using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Topology;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow;

/// <summary>
/// 层次实例关系读取适配器 —— 把「引用图边」翻译为框架的父/子事实，是三层接入 DeclarativeDataflow 的**唯一读实现**。
/// <para>
/// 只读、不写边（写边唯一 owner 仍是 DataStoreDriven.ReferenceGraphCoordinator）；未装配引用端口 → 子层返回空集，
/// 对齐「未装配返回诊断」模式。一个实例固定一个 workflowId（node 层寻址必需，task 层不含该维度）。
/// </para>
/// <para>
/// 反向（子 → 父）查询需引用图入边能力，故经可选端口 <see cref="IResourceReferenceStore"/> 提供；
/// 未装配该端口时反向查询返回 null（引擎正向遍历不依赖它，仅 <c>AncestorsAsync</c> 会用到）。
/// </para>
/// </summary>
public sealed class ReferenceHierarchyRelationReader : IHierarchyRelationReader
{
    private readonly IHierarchyReferenceManager? _reference;
    private readonly IResourceReferenceStore? _referenceStore;
    private readonly string _workflowId;

    /// <param name="reference">三层层级引用管理端口（可空；未装配 → 子层查询返回空集）。</param>
    /// <param name="workflowId">节点层工作流命名空间（空 → 归一化为默认工作流）。</param>
    /// <param name="referenceStore">引用图读写端口（可空；仅反向父查询需要，如 <c>ReferenceGrounding.Graph</c>）。</param>
    public ReferenceHierarchyRelationReader(
        IHierarchyReferenceManager? reference,
        string? workflowId = null,
        IResourceReferenceStore? referenceStore = null)
    {
        _reference = reference;
        _referenceStore = referenceStore;
        _workflowId = NodeOutputKeys.Normalize(workflowId);
    }

    /// <summary>是否已装配引用端口（供调用方判空诊断）。</summary>
    public bool IsAttached => _reference is not null;

    /// <summary>是否支持反向（子 → 父）查询（须已装配引用图读端口）。</summary>
    public bool SupportsReverse => _referenceStore is not null;

    /// <summary>本适配器固定的节点层工作流命名空间。</summary>
    public string WorkflowId => _workflowId;

    /// <inheritdoc />
    /// <remarks>
    /// 领域端口（<see cref="IHierarchyReferenceManager"/>）优先；该端口未覆盖或返回空集的包含边回退到引用图出边读取
    /// （<see cref="IResourceReferenceStore"/>），使任意已声明包含边（含自递归 <c>task_owns_task</c>）同构可跑。
    /// </remarks>
    public async Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (parent.IsEmpty) return Array.Empty<LevelRef>();

        var fromDomain = await ChildrenFromDomainPortAsync(parent, containment, ct).ConfigureAwait(false);
        if (fromDomain.Count > 0) return fromDomain;
        return await ChildrenFromStoreAsync(parent, containment, ct).ConfigureAwait(false);
    }

    /// <summary>领域端口读取（历史 → 任务 / 任务 → 节点两侧已建模语义；其余返回空集）。</summary>
    private async Task<IReadOnlyList<LevelRef>> ChildrenFromDomainPortAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct)
    {
        if (_reference is null) return Array.Empty<LevelRef>();

        if (HierarchyLevelRefs.IsHistory(parent) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.HistoryOwnsTask, StringComparison.Ordinal))
        {
            var taskKeys = await _reference.GetHistoryTaskKeysAsync(parent.Id, ct).ConfigureAwait(false);
            return Map(taskKeys, HierarchyLevelRefs.Task);
        }

        if (HierarchyLevelRefs.IsTask(parent) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.CrossDbLink, StringComparison.Ordinal))
        {
            var nodeKeys = await _reference.GetTaskNodeKeysAsync(parent.Id, _workflowId, ct).ConfigureAwait(false);
            return Map(nodeKeys, key => HierarchyLevelRefs.Node(_workflowId, key));
        }

        return Array.Empty<LevelRef>();
    }

    /// <summary>
    /// 通用子层读取（引用图出边 + 按包含声明的子层投影端点）：覆盖领域端口未建模的包含边
    /// （如自递归 <c>task_owns_task</c>）；未装配引用图读端口 → 空集（不阻断单层直跑）。
    /// </summary>
    private async Task<IReadOnlyList<LevelRef>> ChildrenFromStoreAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct)
    {
        if (_referenceStore is null) return Array.Empty<LevelRef>();
        var parentResource = ParentResourceOf(parent);
        if (parentResource is null) return Array.Empty<LevelRef>();

        var references = await _referenceStore.GetReferencesAsync(parentResource, includeIncoming: false, ct)
            .ConfigureAwait(false);
        var results = new List<LevelRef>();
        var seen = new HashSet<LevelRef>();
        foreach (var reference in references)
        {
            if (reference is null) continue;
            if (!string.Equals(reference.RefType, containment.ContainsEdgeType, StringComparison.Ordinal)) continue;
            if (!IsSameResource(reference.Source, parentResource)) continue;
            if (ProjectChild(containment.ChildLevelId, reference.Target) is not { } child) continue;
            if (child.IsEmpty || !seen.Add(child)) continue;
            results.Add(child);
        }
        return results;
    }

    /// <summary>父端资源标识（按父层判定；未知层返回 null）。</summary>
    private ResourceIdentifier? ParentResourceOf(LevelRef parent)
    {
        if (HierarchyLevelRefs.IsHistory(parent))
            return new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HierarchyResourceKeys.History(parent.Id) };

        if (HierarchyLevelRefs.IsTask(parent))
            return new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = parent.Id };

        if (HierarchyLevelRefs.IsNode(parent))
            return NodeResourceOf(parent);

        return null;
    }

    /// <summary>节点端资源标识（Id = {workflowId}.{nodeKey}，Location = workflowId）；正反向共用同一构造。</summary>
    private ResourceIdentifier NodeResourceOf(LevelRef node)
    {
        var workflowId = WorkflowOf(node);
        return new ResourceIdentifier
        {
            ResourceType = NodeResourceTypes.Node,
            Id = HierarchyResourceKeys.Node(workflowId, node.Id),
            Location = workflowId,
        };
    }

    /// <summary>子端资源 → 层引用（层判定由包含声明的 ChildLevelId 给出，资源类型须一致）。</summary>
    private LevelRef? ProjectChild(string childLevelId, ResourceIdentifier? target)
    {
        if (target is null) return null;

        if (string.Equals(childLevelId, HierarchySchemas.TaskLevel, StringComparison.Ordinal) &&
            string.Equals(target.ResourceType, NodeResourceTypes.Task, StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(target.Id) ? null : HierarchyLevelRefs.Task(target.Id!);

        if (string.Equals(childLevelId, HierarchySchemas.NodeLevel, StringComparison.Ordinal) &&
            string.Equals(target.ResourceType, NodeResourceTypes.Node, StringComparison.Ordinal))
            return HierarchyResourceKeys.TryParseNodeKey(target.Id, _workflowId, out var nodeKey)
                ? HierarchyLevelRefs.Node(_workflowId, nodeKey)
                : null;

        if (string.Equals(childLevelId, HierarchySchemas.HistoryLevel, StringComparison.Ordinal) &&
            string.Equals(target.ResourceType, NodeResourceTypes.History, StringComparison.Ordinal))
            return HierarchyResourceKeys.TryParseHistoryId(target.Id, out var historyId)
                ? HierarchyLevelRefs.History(historyId)
                : null;

        return null;
    }

    /// <summary>节点层工作流命名空间（引用未带作用域时回退适配器默认值）。</summary>
    private string WorkflowOf(LevelRef node)
        => string.IsNullOrWhiteSpace(node.Scope) ? _workflowId : node.Scope!;

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, HierarchyContainment containment, CancellationToken ct = default)
        => ResolveParentAsync(child, containment, parentLevelId: null, ct);

    /// <inheritdoc />
    /// <remarks>以父层 Id 消歧（多父层共用同一包含边类型时）；与基类默认转发不同，此处参与筛选。</remarks>
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

        var childResource = ChildResourceOf(child, containment);
        if (childResource is null) return null;

        var references = await _referenceStore.GetReferencesAsync(childResource, includeIncoming: true, ct)
            .ConfigureAwait(false);
        foreach (var reference in references)
        {
            if (reference is null) continue;
            if (!string.Equals(reference.RefType, containment.ContainsEdgeType, StringComparison.Ordinal)) continue;
            // 以端点相等判定入边（不依赖 IsIncoming 标志，兼容不同读实现）。
            if (!IsSameResource(reference.Target, childResource)) continue;
            if (ProjectParent(child, reference.Source, parentLevelId) is { } parent) return parent;
        }
        return null;
    }

    /// <summary>子端资源标识（节点层带 workflowId 命名空间；任务层即裸 taskKey）；边类型不符返回 null。</summary>
    private ResourceIdentifier? ChildResourceOf(LevelRef child, HierarchyContainment containment)
    {
        if (HierarchyLevelRefs.IsNode(child) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.CrossDbLink, StringComparison.Ordinal))
            return NodeResourceOf(child);

        if (HierarchyLevelRefs.IsTask(child) &&
            string.Equals(containment.ContainsEdgeType, CrossDbReferenceTypes.HistoryOwnsTask, StringComparison.Ordinal))
            return new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = child.Id };

        return null;
    }

    /// <summary>父端资源 → 层引用（层判定与 <paramref name="parentLevelId"/> 一致才接受）。</summary>
    private static LevelRef? ProjectParent(LevelRef child, ResourceIdentifier? source, string? parentLevelId)
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

    private static bool MatchLevel(string? parentLevelId, string expectedLevelId)
        => parentLevelId is null || string.Equals(parentLevelId, expectedLevelId, StringComparison.Ordinal);

    private static bool IsSameResource(ResourceIdentifier? left, ResourceIdentifier right)
        => left is not null
           && string.Equals(left.ResourceType, right.ResourceType, StringComparison.Ordinal)
           && string.Equals(left.Id, right.Id, StringComparison.Ordinal);

    /// <summary>键集 → 层引用集（保序去重，过滤空键）。</summary>
    private static IReadOnlyList<LevelRef> Map(IReadOnlyList<string>? keys, Func<string, LevelRef> project)
    {
        if (keys is not { Count: > 0 }) return Array.Empty<LevelRef>();
        var results = new List<LevelRef>(keys.Count);
        var seen = new HashSet<LevelRef>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var node = project(key);
            if (node.IsEmpty || !seen.Add(node)) continue;
            results.Add(node);
        }
        return results;
    }
}
