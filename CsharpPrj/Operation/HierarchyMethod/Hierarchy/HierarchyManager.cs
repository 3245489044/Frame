using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Hierarchy.Dataflow;
using Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.History.Runtime;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Tasks;
using Operation.WorkFlowMethod.Nodes.Contracts;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Hierarchy;

/// <summary>
/// 三层层级统一管理门面默认实现（LLM-free 组合根）：编排「引用管理（IHierarchyReferenceManager）」与
/// 「三层实例（节点输出 / 任务簿记）」—— 只编排不复制写边原语（SingleImplementation / TH-12）。
/// 装配可空：未装配引用端口 → 查询空集 / 写 no-op；未装配节点/任务端口 → 对应持久化跳过。不阻断 DB-only 路径。
/// <para>
/// 本门面同时是**层次处理引擎的组合根**：三层结构声明为 <see cref="HierarchySchemas.HistoryTaskNode"/> 数据，
/// 遍历 / 终止 / 子结论收敛由 <c>Modules.DeclarativeDataflow</c> 的引擎执法
/// （见 <see cref="IHierarchyManager.ProcessHistoryAsync"/> 等）；运行态端口与引用图读端口可空装配。
/// </para>
/// </summary>
public sealed class HierarchyManager : IHierarchyManager
{
    private readonly IHierarchyReferenceManager? _reference;
    private readonly INodeOutputStore? _nodes;
    private readonly Func<TaskRecord, CancellationToken, Task>? _saveTask;
    private readonly Func<string, CancellationToken, Task>? _deleteTask;
    private readonly Lazy<HierarchyProcessFacade> _processing;
    private readonly Lazy<HierarchyProcessFacade> _taskTreeProcessing;
    // 任务供给簿记流入口（R2）：history→task 归拢的唯一触发点（编排真源 = Config/workflows/task-provision.ushar）。
    private readonly global::Operation.HierarchyMethod.Nodes.Linkage.ITaskProvisionFlow? _taskProvisionFlow;
    // 诊断日志插槽（缺省 no-op）：装配期由组合根回填；簿记流失败经此可见化（ERR-7，禁静默）。
    private readonly Action<string, object?>? _log;

    /// <param name="reference">引用管理端口（可空；实现 = DataStoreDriven.ReferenceGrounding）。本门面及三层实例只依赖此接口、零 LiteDB 具体类型。</param>
    /// <param name="nodes">节点输出存储（可空；ur_nodes NodeType=output）。</param>
    /// <param name="saveTask">任务簿记保存端口（可空；由组合根注入任务池持久化）。</param>
    /// <param name="deleteTask">任务簿记删除端口（可空）。</param>
    /// <param name="runtimePort">单 task 运行状态端口（可空；任务层处理器用）。</param>
    /// <param name="historyRuntime">history 会话编排门面（可空；历史层处理器用）。</param>
    /// <param name="referenceStore">引用图读写端口（可空；反向祖先链查询用，如 ReferenceGrounding.Graph）。</param>
    /// <param name="schema">层次 schema（可空；缺省 = 三层异构 history → task → node）。</param>
    /// <param name="workflowId">节点层工作流命名空间（可空；缺省 = agent-round，对齐 ur_nodes 约定）。</param>
    /// <param name="observer">可选观测（护栏触发 / 进出节点；缺省 = 不可观测，见 S2）。</param>
    /// <param name="aggregator">子层结论聚合器（可空；缺省 = AX.ResultComposition：按子层 round argmax 取最新终态，
    /// 数据面缺失时回退合取，见 S4）。如需改回全合取，显式传 <see cref="Modules.DeclarativeDataflow.Processing.CollectChildResultAggregator.Default"/>。</param>
    /// <param name="runStateProvider">运行态门面（可空；多来源 live/snapshot/derived 合并的唯一入口，经注入由组合根装配）。</param>
    public HierarchyManager(
        IHierarchyReferenceManager? reference = null,
        INodeOutputStore? nodes = null,
        Func<TaskRecord, CancellationToken, Task>? saveTask = null,
        Func<string, CancellationToken, Task>? deleteTask = null,
        IHistoryRuntimePort? runtimePort = null,
        IHistoryRuntimeService? historyRuntime = null,
        IResourceReferenceStore? referenceStore = null,
        HierarchySchema? schema = null,
        string? workflowId = null,
        ILevelProcessObserver? observer = null,
        IChildResultAggregator? aggregator = null,
        IRunStateProvider? runStateProvider = null,
        global::Operation.HierarchyMethod.Nodes.Linkage.ITaskProvisionFlow? taskProvisionFlow = null,
        Action<string, object?>? log = null)
    {
        _reference = reference;
        _nodes = nodes;
        _saveTask = saveTask;
        _deleteTask = deleteTask;
        _taskProvisionFlow = taskProvisionFlow;
        _log = log;
        // S4：默认启用 AX.ResultComposition（子层 round argmax 取最新终态；数据面缺失回退合取）。
        // 可选参数默认值须为编译时常量，故以 null 占位、此处回退到 ResultCompositionAggregator。
        var effectiveAggregator = aggregator ?? ResultCompositionAggregator.Default;
        _processing = new Lazy<HierarchyProcessFacade>(() => HierarchyProcessFacade.Create(
            reference: _reference,
            nodeOutputs: _nodes,
            runtimePort: runtimePort,
            historyRuntime: historyRuntime,
            workflowId: workflowId,
            schema: schema ?? HierarchySchemas.HistoryTaskNode,
            observer: observer,
            aggregator: effectiveAggregator,
            referenceStore: referenceStore,
            runStateProvider: runStateProvider));
        // 任务自递归层次（task → sub-task → … → node）单独装配：schema 不同（含自环），端口与上者同源。
        _taskTreeProcessing = new Lazy<HierarchyProcessFacade>(() => HierarchyProcessFacade.Create(
            reference: _reference,
            nodeOutputs: _nodes,
            runtimePort: runtimePort,
            historyRuntime: historyRuntime,
            workflowId: workflowId,
            schema: HierarchySchemas.TaskTree(CrossDbReferenceTypes.TaskOwnsTask),
            observer: observer,
            aggregator: effectiveAggregator,
            referenceStore: referenceStore,
            runStateProvider: runStateProvider));
    }

    public async Task<HistoryRecord> CreateHistoryAsync(string historyId, string? name = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        if (_reference is not null)
            await _reference.EnsureHistoryAsync(historyId, ct).ConfigureAwait(false);
        return new HistoryRecord(historyId, name ?? historyId);
    }

    public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default)
        => _reference is null ? Task.FromResult(false) : _reference.DeleteHistoryAsync(historyId, ct);

    public async Task RegisterTaskAsync(string taskKey, string? historyId = null, TaskRecord? spec = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        var record = spec ?? new TaskRecord();
        record.TaskKey = taskKey;
        if (!string.IsNullOrWhiteSpace(historyId))
            record.HistoryId = historyId;

        if (_saveTask is not null)
            await _saveTask(record, ct).ConfigureAwait(false);

        // 归拢到 history（R2 归流）：经**任务供给簿记流入口**触发（编排真源 = task-provision.ushar 的
        // [Flow] TaskProvision）；本处只是唯一触发点，不持顺序/入参。
        // DEBT-2 收口：生产装配点（ConversationHosting）已注入本端口，故迁移期回退支删除。
        // ERR-7：流返回 false ⇒ 可见化（禁静默装作已归拢）。
        if (_taskProvisionFlow is not null && !string.IsNullOrWhiteSpace(historyId))
        {
            if (!await _taskProvisionFlow.ProvisionAsync(historyId, null, taskKey, ct).ConfigureAwait(false))
            {
                _log?.Invoke("HierarchyManager_provision_failed", new
                {
                    historyId,
                    taskKey,
                    reason = "任务供给簿记流返回 false：容器构建 / 归拢未完成",
                });
            }
        }
    }

    public async Task DeleteTaskAsync(string taskKey, CancellationToken ct = default)
    {
        if (_deleteTask is not null)
            await _deleteTask(taskKey, ct).ConfigureAwait(false);
        // 引用侧 task 端点镜像与入/出边的级联清理由注入的删除端口承担（Agent 侧 WriteThroughTaskPoolStore.DeleteAsync
        // → ReferenceGrounding.RemoveTaskMirrorAsync）；本门面不重复写边（SingleImplementation / TH-12）。
    }

    public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _reference is null ? Task.CompletedTask : _reference.LinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _reference is null ? Task.CompletedTask : _reference.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetTasksAsync(string historyId, CancellationToken ct = default)
        => MemberKeysAsync(
            HierarchyLevelRefs.History(historyId),
            HierarchySchemas.TaskLevel,
            () => _reference!.GetHistoryTaskKeysAsync(historyId, ct));

    public Task<IReadOnlyList<string>> GetNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => MemberKeysAsync(
            HierarchyLevelRefs.Task(taskKey),
            HierarchySchemas.NodeLevel,
            () => _reference!.GetTaskNodeKeysAsync(taskKey, workflowId, ct));

    public Task<IReadOnlyList<string>> GetHistoryNodesAsync(string historyId, string workflowId, CancellationToken ct = default)
        => MemberKeysAsync(
            HierarchyLevelRefs.History(historyId),
            HierarchySchemas.NodeLevel,
            () => _reference!.GetHistoryNodeKeysAsync(historyId, workflowId, ct));

    /// <summary>
    /// 成员集查询（**无界**：不施加展开预算，仅按实体去重）——优先经声明式引擎（与
    /// <see cref="ProcessHistoryAsync"/> 共用层序 / 环守卫 / 去重的**单一遍历实现**），
    /// 引擎无法表达该查询时回退 owner 侧直读端口。
    /// <para>
    /// 引擎路径仅在「起点层已声明 <b>且</b> 目标层自根可达」时启用；否则（如任务自递归 schema 下以 history 为起点）
    /// 回退 <paramref name="fallback"/>，保持成员集查询的 **schema 无关** 语义。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> MemberKeysAsync(
        LevelRef root, string targetLevelId, Func<Task<IReadOnlyList<string>>> fallback)
    {
        if (_reference is null) return Array.Empty<string>();

        if (CanExpress(root.LevelId, targetLevelId))
        {
            var nodes = await _processing.Value
                .DescendantsAsync(root, stopLevelId: null, maxDepth: UnboundedDepth)
                .ConfigureAwait(false);
            return nodes
                .Where(n => string.Equals(n.LevelId, targetLevelId, StringComparison.Ordinal))
                .Select(n => n.Id)
                .ToList();
        }

        return await fallback().ConfigureAwait(false);
    }

    /// <summary>该层次 schema 能否表达「起点层 → 目标层」的展开（起点层须已声明且目标层自根可达）。</summary>
    private bool CanExpress(string rootLevelId, string targetLevelId)
    {
        var schema = _processing.Value.Schema;
        return schema.Level(rootLevelId) is not null
            && schema.LevelOrderFromRoot().Contains(targetLevelId, StringComparer.Ordinal);
    }

    /// <summary>成员集查询的无界深度（终止由按实体去重保证，故不设截断——对齐「成员集查询无上限」契约）。</summary>
    private const int UnboundedDepth = int.MaxValue;

    public Task PersistNodeOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
        => _nodes is null ? Task.CompletedTask : _nodes.WriteOutputAsync(workflowId, nodeKey, port, value, ct);

    /// <inheritdoc />
    public Task<LevelProcessResult> ProcessHistoryAsync(string historyId, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        return _processing.Value.ProcessHistoryAsync(historyId, plan, ct);
    }

    /// <inheritdoc />
    public Task<LevelProcessResult> ProcessTaskAsync(string taskKey, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        return _processing.Value.ProcessTaskAsync(taskKey, plan, ct);
    }

    /// <inheritdoc />
    public Task<LevelProcessResult> ProcessTaskTreeAsync(string taskKey, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        return _taskTreeProcessing.Value.ProcessAsync(HierarchyLevelRefs.Task(taskKey), plan, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LevelRef>> DescendantsAsync(LevelRef node, string? stopLevelId = null, int maxDepth = 64, CancellationToken ct = default)
        => _processing.Value.DescendantsAsync(node, stopLevelId, maxDepth, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<LevelRef>> AncestorsAsync(LevelRef node, CancellationToken ct = default)
        => _processing.Value.AncestorsAsync(node, ct);
}
