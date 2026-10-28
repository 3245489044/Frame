using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Topology;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Hierarchy.Dataflow.Readers;
using Operation.HierarchyMethod.Nodes;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow;

/// <summary>
/// 层次实例关系读取适配器 —— 把「领域端口 + 引用图边」翻译为框架的父/子事实，是三层接入
/// DeclarativeDataflow 的**唯一读实现**。
/// <para>
/// 结构（策略单点）：**领域端口读取器**（已建模包含边）+ **引用图读取器**（任意已声明包含边的出/入边）
/// 经内核 <see cref="CompositeHierarchyRelationReader"/> 组合 —— 「主源空则问备源」不再由本类手写。
/// 故 history/task 两侧已建模语义优先；未覆盖 / 为空的包含边（含自递归 <c>task_owns_task</c>）回退引用图。
/// </para>
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
    private readonly HierarchyDomainPortReader _domain;
    private readonly ReferenceGraphHierarchyReader _graph;
    private readonly CompositeHierarchyRelationReader _composite;
    private readonly string _workflowId;

    /// <param name="reference">三层层级引用管理端口（可空；未装配 → 子层查询返回空集）。</param>
    /// <param name="workflowId">节点层工作流命名空间（空 → 归一化为默认工作流）。</param>
    /// <param name="referenceStore">引用图读写端口（可空；仅反向父查询需要，如 <c>ReferenceGrounding.Graph</c>）。</param>
    public ReferenceHierarchyRelationReader(
        IContainerMemberPort? reference,
        string? workflowId = null,
        IResourceReferenceStore? referenceStore = null)
    {
        _workflowId = NodeOutputKeys.Normalize(workflowId);
        _domain = new HierarchyDomainPortReader(reference, _workflowId);
        _graph = new ReferenceGraphHierarchyReader(referenceStore, _workflowId);
        _composite = new CompositeHierarchyRelationReader(_domain, _graph);
    }

    /// <summary>是否已装配引用端口（供调用方判空诊断）。</summary>
    public bool IsAttached => _domain.IsAttached;

    /// <summary>是否支持反向（子 → 父）查询（须已装配引用图读端口）。</summary>
    public bool SupportsReverse => _graph.SupportsReverse;

    /// <summary>本适配器固定的节点层工作流命名空间。</summary>
    public string WorkflowId => _workflowId;

    /// <inheritdoc />
    /// <remarks>领域端口优先；未覆盖 / 返回空集的包含边回退引用图出边读取（组合器单一实现）。</remarks>
    public Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct = default)
        => _composite.ChildrenOfAsync(parent, containment, ct);

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, HierarchyContainment containment, CancellationToken ct = default)
        => _composite.ParentOfAsync(child, containment, ct);

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, string parentLevelId, HierarchyContainment containment, CancellationToken ct = default)
        => _composite.ParentOfAsync(child, parentLevelId, containment, ct);
}
