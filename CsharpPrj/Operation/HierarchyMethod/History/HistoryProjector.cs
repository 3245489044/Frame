using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Tasks;
namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史投影器默认实现（History 层，LLM-free）：把一轮编排结果沉淀到历史容器并回喂为下轮上下文。
/// 写边原语统一委托 <see cref="IHistoryRelationManager"/>（history_owns_task / session_owns；顺序 = 键内 (round, seq)），
/// 本实现只编排不复制写边（SingleImplementation / TH-12）。装配可空：未装配关系管理器 → 沉淀/回读均 no-op，
/// 对齐「未装配返回诊断、不阻断 DB-only 路径」模式。Agent 可注入本实现或替换为 Conversation 历史适配器。
/// </summary>
public sealed class HistoryProjector : IHistoryProjector
{
    private readonly IHistoryRelationManager? _relations;
    // 任务供给簿记流入口（R2）：history→task 归拢的唯一触发点（编排真源 = Config/workflows/task-provision.ushar）。
    // null ⇒ 迁移期沿用关系端口直调（退役条件 = 全部装配点提供本入口后删回退支）。
    private readonly Nodes.Linkage.ITaskProvisionFlow? _taskProvisionFlow;
    // 归属边簿记流入口（R1）：cross_db_link（task→node）的唯一触发点（编排真源 = Config/workflows/attribution.ushar）。
    // null ⇒ 迁移期沿用关系端口直调（退役条件 = 全部装配点提供本入口后删回退支）。
    private readonly Nodes.Linkage.INodeAttributionFlow? _attributionFlow;
    // 诊断可见化端口（缺省空实现）：装配期由组合根回填；簿记流失败 / 回退经此可见化（ERR-7，禁静默）。
    private readonly IDiagnosticsSink? _diagnostics;
    // 迁移期回退姿态（装配期一处选定）：调用点只写正路，回退与否由策略对象裁决（DEBT-1 收敛）。
    private readonly Nodes.Linkage.FlowPosture _posture;

    /// <param name="relations">历史关系管理端口（可空；未装配则沉淀/回读均跳过）。</param>
    /// <param name="taskProvisionFlow">任务供给簿记流入口（R2，可空）：装配后「建容器 + 归拢 task」经声明流写。</param>
    /// <param name="attributionFlow">归属边簿记流入口（R1，可空）：装配后「task→node 归属边」经声明流写。</param>
    /// <param name="diagnostics">诊断可见化端口（可空 = 不记）；簿记流失败 / 回退经此可见化。</param>
    /// <param name="posture">迁移期回退姿态（可空 = <see cref="Nodes.Linkage.FlowPosture.AllowLegacyFallback"/>，零行为变化）。</param>
    public HistoryProjector(
        IHistoryRelationManager? relations = null,
        Nodes.Linkage.ITaskProvisionFlow? taskProvisionFlow = null,
        Nodes.Linkage.INodeAttributionFlow? attributionFlow = null,
        IDiagnosticsSink? diagnostics = null,
        Nodes.Linkage.FlowPosture? posture = null)
    {
        _relations = relations;
        _taskProvisionFlow = taskProvisionFlow;
        _attributionFlow = attributionFlow;
        _diagnostics = diagnostics;
        _posture = posture ?? Nodes.Linkage.FlowPosture.AllowLegacyFallback;
    }

    /// <summary>
    /// 沉淀一轮：幂等建历史容器 → 归拢任务（history_owns_task）→ 挂载节点（session_owns）；
    /// 历史顺序 = 键内 (round, seq)（round_sequence 边已退役，A4′）。
    /// 历史容器 Id 规则（R3）由 <see cref="Reference.HistoryContainerRules"/> 承载（声明面对账单源）。
    /// </summary>
    public async Task PersistRoundAsync(NodeRoundResult round, TaskScope scope, CancellationToken ct = default)
    {
        if (_relations is null || scope is null) return;

        var historyId = Nodes.Keys.KeyAddressing.ContainerId(scope.HistoryId, scope.TaskKey);
        var taskKey = scope.TaskKey;
        // ERR-7：供给流失败 ⇒ 可见化并停止挂边（容器未建，挂边只会散落到无名端点）。
        if (!await ProvisionAsync(historyId, taskKey, ct).ConfigureAwait(false))
            return;

        // 节点即结果：逐条以 task→node M:N 关联挂载（历史节点集派生 = 其任务节点并集，不再直写 history→node 边）。
        // 注：消息时序边 round_sequence 已退役（A4′）——历史顺序改由键内 (round, seq) 承载
        // （SessionMessageKeys.OrderComparer 读侧排序，见 RefHead.Project），本处不再写时序边。
        foreach (var rec in round?.Records ?? Array.Empty<NodeExecutionRecord>())
        {
            if (string.IsNullOrWhiteSpace(rec.NodeKey)) continue;
            if (string.IsNullOrWhiteSpace(taskKey)) continue;
            // 命名空间取**存储命名空间**（与落库侧同一权威）；不消费 rec.WorkflowId 以免来源名泄漏入图端点。
            // 坐标单源（DUP-8/ERR-3）：经 KeyAddressing（显式命名缺省），不再散写 Normalize(null)/常量。
            var workflowId = Nodes.Keys.KeyAddressing.DefaultNamespace;
            // R1 归流：归属边经**归属边簿记流入口**触发（编排真源 = attribution.ushar 的 [Flow] NodeAttribution）；
            // 流入口缺失 / 失败由**回退姿态**裁决（迁移期直调回退 / 收口期拒绝），本处不再写 if/else（DEBT-1）。
            var node = rec.NodeKey;
            var outcome = await _posture.RunAsync(
                flow: _attributionFlow is null
                    ? null
                    : () => _attributionFlow.AttachAsync(taskKey, workflowId, node, ct),
                legacy: () => _relations.LinkNodeAsync(taskKey, workflowId, node, ct),
                diagnostics: _diagnostics,
                context: "HistoryProjector_attribution",
                payload: new { taskKey, workflowId, nodeKey = node }).ConfigureAwait(false);
            if (!outcome.Ok) break; // 收口姿态下缺入口 ⇒ 停止挂边（禁向无名端点散落）
        }
    }

    // 入史物化（原 ProjectMarkedAsync，含 fail-fast 桩）已退役（DEBT-4 残留收口）：标记节点 → L-B
    // conversation_message 的唯一写路径改经声明流 round-attribution.ushar 的 fn=project_marked
    // （入史落点 = RoundMessageSettlement），本默认投影器只承担 PersistRoundAsync / BuildContextAsync。

    /// <summary>
    /// 从某 task 累积历史重建下一轮执行上下文（事务无关的只读重建）。本默认实现仅回喂已归拢任务键集；
    /// 完整历史投影（L0/L1/L2 提示词上下文）由 <see cref="IContextProjector"/> 层（阶段五）完成。
    /// </summary>
    /// <remarks>
    /// 统一 Id 规则（<see cref="ResolveHistoryId"/>）：无显式 HistoryId 时 historyId == taskKey。
    /// 若沉淀（<see cref="PersistRoundAsync"/>）使用了显式 <see cref="TaskScope.HistoryId"/>，
    /// 则回读须以同一容器键发起（本方法参数即容器键），否则历史回读与沉淀不对称。
    /// </remarks>
    public async Task<NodeRoundInput> BuildContextAsync(string taskKey, string? sessionId, CancellationToken ct = default)
    {
        var vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (_relations is not null && !string.IsNullOrWhiteSpace(taskKey))
        {
            // 以容器键（按声明面规则 = 缺省回退 taskKey）枚举归拢任务键（历史回读的轻量形态）。
            var historyId = Nodes.Keys.KeyAddressing.ContainerId(null, taskKey);
            var keys = await _relations.GetTaskKeysAsync(historyId, ct).ConfigureAwait(false);
            if (keys is { Count: > 0 })
                vars["history_task_keys"] = keys;
        }

        return new NodeRoundInput(taskKey, sessionId, 0, vars);
    }

    /// <summary>
    /// 建历史容器 + 归拢 task（R2）：经**任务供给簿记流入口**触发（编排真源 = task-provision.ushar 的
    /// [Flow] TaskProvision）；流入口缺失按**回退姿态**裁决（迁移期直调 / 收口期拒绝），本处不写 if/else。
    /// </summary>
    /// <returns>true = 已提交或合法 no-op；false = 供给流未装配执行器 / 流失败（ERR-7：调用方按可见化处理）。</returns>
    private async Task<bool> ProvisionAsync(string historyId, string taskKey, CancellationToken ct)
    {
        var outcome = await _posture.RunAsync(
            flow: _taskProvisionFlow is null
                ? null
                : () => _taskProvisionFlow.ProvisionAsync(historyId, null, taskKey, ct),
            legacy: () => ProvisionLegacyAsync(historyId, taskKey, ct),
            diagnostics: _diagnostics,
            context: "HistoryProjector_provision",
            payload: new { historyId, taskKey }).ConfigureAwait(false);
        return outcome.Ok;
    }

    /// <summary>迁移期回退直调（仅 <see cref="Nodes.Linkage.FlowPosture.AllowLegacyFallback"/> 且流入口缺失时可达）。</summary>
    private async Task ProvisionLegacyAsync(string historyId, string taskKey, CancellationToken ct)
    {
        await _relations!.CreateAsync(historyId, ct: ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(taskKey))
            await _relations.AttachTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);
    }
}
