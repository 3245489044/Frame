using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.History;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// nodes / task / history 三层关系的高层编排门面（统一入口）—— 收敛散落在 UI 库 / 任务池 / 会话层的
/// 「建历史 + 归拢 task + 关联 node」重复逻辑为确定性复合编排（SingleImplementation / TH-12）。
/// <para>
/// 职责边界：本门面**只编排、不写边**——写边原语仍委托 <see cref="IHistoryRelationManager"/>
/// （→ IHistoryRelationManager → IReferenceMaintenance → 图协调器），不复制任何写边逻辑。
/// 装配可空：未装配关系守卫时各方法 no-op / 返回诊断，不阻断 DB-only 路径。
/// </para>
/// <para>
/// 统一 Id 规则：**historyId == taskKey（会话名）**，与 <see cref="ConversationPipeline"/> 的历史归拢语义一致，
/// 消除旧 UI 层「default」与 pipeline 层「sessionName」双历史容器的二义（同一 task 被归拢到两个 history 的矛盾）。
/// </para>
/// </summary>
public sealed class NodeTaskHistoryService
{
    private readonly IHistoryRelationManager? _relations;
    // 任务供给簿记流入口（R2）：history→task 归拢的唯一触发点（编排真源 = Config/workflows/task-provision.ushar）。
    // null ⇒ 诊断可见化（迁移期直调回退支已退役，批次 C）。
    private readonly Nodes.Linkage.ITaskProvisionFlow? _taskProvisionFlow;
    // 归属边簿记流入口（R1）：cross_db_link（task→node）的唯一触发点（编排真源 = Config/workflows/attribution.ushar）。
    // null ⇒ 诊断可见化（迁移期直调回退支已退役，批次 C）。
    private readonly Nodes.Linkage.INodeAttributionFlow? _attributionFlow;
    // 诊断可见化端口（缺省空实现）：装配期由组合根回填（分层无环——框架不感知 Agent 诊断类型）。
    // 消费方：簿记流未装配 / ProvisionAsync=false 的失败可见化（ERR-7，禁静默）。
    private readonly IDiagnosticsSink? _diagnostics;
    // 迁移期回退姿态（装配期一处选定）：调用点只写正路，回退与否由策略对象裁决（DEBT-1 收敛）。
    private readonly Nodes.Linkage.FlowPosture _posture;
    // task→node 归属边撤销原语（节点层写守卫；分层收口：node 边不经 history 关系端口）。
    // null ⇒ 未装配（detach no-op，与「未装配返回诊断」模式一致）。
    private readonly IReferenceMaintenance? _references;

    /// <param name="relations">历史关系管理端口（可空；未装配则 history 层各方法 no-op）。</param>
    /// <param name="taskProvisionFlow">任务供给簿记流入口（R2，可空）：装配后「建容器 + 归拢 task」经声明流写。</param>
    /// <param name="attributionFlow">归属边簿记流入口（R1，可空）：装配后「task→node 归属边」经声明流写。</param>
    /// <param name="diagnostics">诊断可见化端口（可空 = 不记）；簿记流失败 / 回退经此可见化。</param>
    /// <param name="posture">迁移期回退姿态（可空 = <see cref="Nodes.Linkage.FlowPosture.AllowLegacyFallback"/>，零行为变化）。</param>
    /// <param name="references">节点边撤销原语（节点层写守卫，可空）：detach 不经 history 关系端口（分层收口）。</param>
    public NodeTaskHistoryService(
        IHistoryRelationManager? relations,
        Nodes.Linkage.ITaskProvisionFlow? taskProvisionFlow = null,
        Nodes.Linkage.INodeAttributionFlow? attributionFlow = null,
        IDiagnosticsSink? diagnostics = null,
        Nodes.Linkage.FlowPosture? posture = null,
        IReferenceMaintenance? references = null)
    {
        _relations = relations;
        _taskProvisionFlow = taskProvisionFlow;
        _attributionFlow = attributionFlow;
        _diagnostics = diagnostics;
        _posture = posture ?? Nodes.Linkage.FlowPosture.AllowLegacyFallback;
        _references = references;
    }

    /// <summary>是否已装配关系守卫（供调用方判空诊断）。</summary>
    public bool IsAttached => _relations is not null;

    /// <summary>幂等建空历史根容器（history.{historyId}，History=Task 上层 class 容器）。不归拢任何 task。
    /// 未装配关系守卫 → no-op。historyId 空 → no-op。</summary>
    /// <param name="historyId">历史容器 Id（任意识别名；如 "history:analysis"）。</param>
    /// <param name="name">历史展示名（缺省 = historyId）。</param>
    public async Task EnsureHistoryAsync(string historyId, string? name = null, CancellationToken ct = default)
    {
        if (_relations is null || string.IsNullOrWhiteSpace(historyId)) return;
        var hid = historyId.Trim();
        // 展示名缺省规则单源（ERR-9）：显式 name 优先，缺省 = 容器 Id（禁本层再写三元兜底）。
        await _relations.CreateAsync(hid, name: Nodes.Keys.KeyAddressing.ContainerName(name, hid), ct).ConfigureAwait(false);
    }

    /// <summary>把指定 task 归拢到指定历史（history_owns_task 边；幂等）。historyId/taskKey 空 → no-op。</summary>
    public Task AttachTaskToHistoryAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _relations is null || string.IsNullOrWhiteSpace(historyId) || string.IsNullOrWhiteSpace(taskKey)
            ? Task.CompletedTask
            : _relations.AttachTaskAsync(historyId.Trim(), taskKey.Trim(), ct);

    /// <summary>解除指定 task 从指定历史的归拢（移除 history_owns_task 边；幂等）。未装配关系守卫 → no-op。</summary>
    public Task DetachTaskFromHistoryAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _relations is null || string.IsNullOrWhiteSpace(historyId) || string.IsNullOrWhiteSpace(taskKey)
            ? Task.CompletedTask
            : _relations.DetachTaskAsync(historyId.Trim(), taskKey.Trim(), ct);

    /// <summary>
    /// 确保历史容器存在并归拢指定 task（history_owns_task 边；幂等）。
    /// 容器 Id 规则（R3）= <see cref="HistoryContainerRules"/>：**显式 historyId 优先，缺省回退 taskKey**
    /// （声明面 Config\workflows\task-provision.ushar 的 `[Space] HistoryContainerRule` 对账单源）。
    /// 编排（建容器 → 归拢 task 的**顺序**）由声明流 `[Flow] TaskProvision` 承载（R2）；未装配流入口 ⇒ 迁移期回退直调。
    /// <para>
    /// ERR-1 收口：本重载接受**显式容器 Id**，与任务池/公开动作面（`task_orchestrate create` 的 history_id）同口径 ——
    /// 同一 task 不得被两条路径分别归拢到 `historyId` 与 `taskKey` 两个容器（「同 task 不可双容器」不变式）。
    /// </para>
    /// </summary>
    /// <param name="historyId">显式历史容器 Id（可空；空 ⇒ 按声明面规则回退 <paramref name="taskKey"/>）。</param>
    /// <param name="taskKey">任务键（= 会话名；容器 Id 的缺省回退来源）。</param>
    public async Task EnsureTaskHistoryBoundAsync(string? historyId, string taskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return;
        var hid = Nodes.Keys.KeyAddressing.ContainerId(historyId, taskKey);

        // R2 归流：编排真源 = task-provision.ushar 的 [Flow] TaskProvision（本处只是唯一触发点，不持顺序）。
        // 流入口缺失 / 失败由**回退姿态**裁决（迁移期直调 / 收口期拒绝），本处不再写 if/else（DEBT-1）。
        await _posture.RunAsync(
            _taskProvisionFlow, hid, hid, hid,
            _diagnostics, "NodeTaskHistoryService_provision",
            new { historyId = hid, taskKey }, ct).ConfigureAwait(false);
    }

    /// <summary>缺省容器 Id 重载（historyId == taskKey）：等价 <c>EnsureTaskHistoryBoundAsync(null, taskKey, ct)</c>。</summary>
    /// <param name="taskKey">任务键（= 会话名）。</param>
    public Task EnsureTaskHistoryBoundAsync(string taskKey, CancellationToken ct = default)
        => EnsureTaskHistoryBoundAsync(null, taskKey, ct);

    /// <summary>
    /// task → node 关联（cross_db_link，真多对多；幂等）。历史节点集经此边派生（= 该历史全部任务节点并集）。
    /// R1 归流：经**归属边簿记流入口**触发（编排真源 = attribution.ushar 的 [Flow] NodeAttribution；
    /// 本处只是唯一触发点，不持顺序/入参）。未装配入口（迁移期）⇒ 沿用关系端口直调回退，不静默；
    /// 未装配关系守卫 → no-op。
    /// </summary>
    public async Task AttachNodeToTaskAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey) || string.IsNullOrWhiteSpace(nodeKey)) return;

        // R1 归流：经**归属边簿记流入口**触发（编排真源 = attribution.ushar 的 [Flow] NodeAttribution）；
        // 流入口缺失 / 失败由**回退姿态**裁决（迁移期直调 / 收口期拒绝），本处不再写 if/else（DEBT-1）。
        await _posture.RunAsync(
            _attributionFlow, taskKey.Trim(), workflowId, nodeKey,
            _diagnostics, "NodeTaskHistoryService_attribution",
            new { taskKey, workflowId, nodeKey }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 解除 task → node 关联（M:N 撤销边；幂等）。仅解除本 task 归属，不影响其它 task 共享引用。
    /// 分层收口：node 边撤销经**节点层写守卫** <see cref="IReferenceMaintenance"/>（不经 history 关系端口）。
    /// 与 <see cref="AttachNodeToTaskAsync"/> 成对（Link/Unlink）。未装配守卫 → no-op。
    /// </summary>
    public Task DetachNodeFromTaskAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _references is null || string.IsNullOrWhiteSpace(taskKey) || string.IsNullOrWhiteSpace(nodeKey)
            ? Task.CompletedTask
            : _references.UnlinkTaskNodeAsync(taskKey.Trim(), workflowId, nodeKey, ct);
}
