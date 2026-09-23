using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Modules.DeclarativeDataflow.Topology;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Hierarchy.Dataflow.LevelProcessors;
using Operation.HierarchyMethod.History.Runtime;
using Operation.WorkFlowMethod.Nodes.Contracts;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

using Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow;

/// <summary>
/// 三层层次处理的组合根（LLM-free）—— 装配「层次结构（schema）+ 实例关系读取 + 层处理器」为一台引擎，
/// 供消费方按历史 / 任务为根发起一次多层处理（遍历 / 三重护栏 / 子结论收敛全部复用框架，不手写）。
/// <para>
/// 只读、不写边：写边唯一 owner 仍是 DataStoreDriven.ReferenceGraphCoordinator（SingleImplementation / TH-12）；
/// 本门面不改变三层实例与 <c>IHierarchyManager</c> 的既有职责，二者并列。
/// </para>
/// </summary>
public sealed class HierarchyProcessFacade
{
    private readonly RelationalHierarchyTopology _topology;
    private readonly IHierarchyProcessEngine _engine;

    public HierarchyProcessFacade(
        RelationalHierarchyTopology topology,
        ILevelProcessorRegistry? processors = null,
        IChildResultAggregator? aggregator = null,
        ILevelProcessObserver? observer = null)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _engine = new HierarchyProcessEngine(_topology, processors ?? LevelProcessorRegistry.Empty, aggregator, observer);
    }

    /// <summary>处理引擎（需要自定义计划 / 直接消费根结论时使用）。</summary>
    public IHierarchyProcessEngine Engine => _engine;

    /// <summary>生效的层次 schema。</summary>
    public HierarchySchema Schema => _topology.RawSchema;

    /// <summary>以任意层节点为根处理整棵层次。</summary>
    public Task<LevelProcessResult> ProcessAsync(
        LevelRef root, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
        => _engine.ProcessAsync(root, plan, ct);

    /// <summary>以历史容器为根处理（history → task → node）。</summary>
    public Task<LevelProcessResult> ProcessHistoryAsync(
        string historyId, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
        => ProcessAsync(HierarchyLevelRefs.History(historyId), plan, ct);

    /// <summary>以任务为根处理（task → node）。</summary>
    public Task<LevelProcessResult> ProcessTaskAsync(
        string taskKey, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
        => ProcessAsync(HierarchyLevelRefs.Task(taskKey), plan, ct);

    /// <summary>
    /// 取祖先链（自近及远，如 node → task → history）；需装配 referenceStore，否则返回空链。
    /// </summary>
    public Task<IReadOnlyList<LevelRef>> AncestorsAsync(LevelRef node, CancellationToken ct = default)
        => _topology.AncestorsAsync(node, ct);

    /// <summary>
    /// 取后代集合（广度优先、按实体去重；到达 <paramref name="stopLevelId"/> 不再向下展开）。
    /// </summary>
    public Task<IReadOnlyList<LevelRef>> DescendantsAsync(
        LevelRef node, string? stopLevelId = null, int maxDepth = 64, CancellationToken ct = default)
        => _topology.DescendantsAsync(node, stopLevelId, maxDepth, ct);

    /// <summary>
    /// 按三层默认 schema 与默认处理器装配门面（全部端口可空，未装配即透传诊断）。
    /// </summary>
    /// <param name="reference">三层层级引用管理端口（子层读取来源）。</param>
    /// <param name="nodeOutputs">节点输出存储（节点层处理器使用）。</param>
    /// <param name="runtimePort">单实体运行状态端口（任务层处理器使用）。</param>
    /// <param name="historyRuntime">history 会话编排门面（历史层处理器使用）。</param>
    /// <param name="runStateProvider">运行态门面（任务层处理器使用；多来源 live/snapshot 合并的唯一入口，可空）。</param>
    /// <param name="workflowId">节点层工作流命名空间（空 → 默认工作流）。</param>
    /// <param name="schema">层次 schema（缺省 = <see cref="HierarchySchemas.HistoryTaskNode"/>）。</param>
    /// <param name="aggregator">子层结论聚合器（缺省 = 合取）。</param>
    /// <param name="observer">可选观测（诊断 / 测试）。</param>
    /// <param name="referenceStore">引用图读写端口（可空；仅反向父查询 <c>AncestorsAsync</c> 需要，如 <c>ReferenceGrounding.Graph</c>）。</param>
    /// <remarks>
    /// 典型装配（见 DataStoreDriven）：<c>Create(reference: grounding, nodeOutputs: outputs, runtimePort: runtime, historyRuntime: historyRuntime, workflowId: wf, referenceStore: grounding.Graph)</c>
    /// —— 同一守卫实例既作层级引用端口又提供图读面，无需额外适配。
    /// </remarks>
    public static HierarchyProcessFacade Create(
        IHierarchyReferenceManager? reference = null,
        INodeOutputStore? nodeOutputs = null,
        IHistoryRuntimePort? runtimePort = null,
        IHistoryRuntimeService? historyRuntime = null,
        string? workflowId = null,
        HierarchySchema? schema = null,
        IChildResultAggregator? aggregator = null,
        ILevelProcessObserver? observer = null,
        IResourceReferenceStore? referenceStore = null,
        IRunStateProvider? runStateProvider = null)
    {
        var topology = new RelationalHierarchyTopology(
            new SchemaTopology(schema ?? HierarchySchemas.HistoryTaskNode),
            new ReferenceHierarchyRelationReader(reference, workflowId, referenceStore));

        var processors = new LevelProcessorRegistry(
            new HistoryOverviewLevelProcessor(historyRuntime),
            new TaskStatusLevelProcessor(runtimePort, runStateProvider),
            new NodeOutputLevelProcessor(nodeOutputs, workflowId));

        return new HierarchyProcessFacade(topology, processors, aggregator, observer);
    }
}
