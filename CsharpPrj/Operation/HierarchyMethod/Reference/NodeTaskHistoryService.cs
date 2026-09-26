using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.History;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// nodes / task / history 三层关系的高层编排门面（统一入口）—— 收敛散落在 UI 库 / 任务池 / 会话层的
/// 「建历史 + 归拢 task + 关联 node」重复逻辑为确定性复合编排（SingleImplementation / TH-12）。
/// <para>
/// 职责边界：本门面**只编排、不写边**——写边原语仍委托 <see cref="IHistoryRelationManager"/>
/// （→ HistoryRelationGateway → IReferenceMaintenance → 图协调器），不复制任何写边逻辑。
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
    // null ⇒ 迁移期沿用关系端口直调（退役条件 = 全部装配点提供本入口后删回退支）。
    private readonly Nodes.Linkage.ITaskProvisionFlow? _taskProvisionFlow;

    /// <param name="relations">历史关系管理端口（可空；未装配则各方法 no-op）。</param>
    /// <param name="taskProvisionFlow">任务供给簿记流入口（R2，可空）：装配后「建容器 + 归拢 task」经声明流写。</param>
    public NodeTaskHistoryService(
        IHistoryRelationManager? relations,
        Nodes.Linkage.ITaskProvisionFlow? taskProvisionFlow = null)
    {
        _relations = relations;
        _taskProvisionFlow = taskProvisionFlow;
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
        await _relations.CreateAsync(hid, name: string.IsNullOrWhiteSpace(name) ? hid : name, ct).ConfigureAwait(false);
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
    /// 确保历史容器存在并归拢指定 task（history_owns_task 边；幂等）。**统一归拢入口**——
    /// 替代散落在 <c>EnsureHistoryTaskAsync</c> / <c>ConversationAttachTaskAsync</c> 的三处重复
    /// <c>CreateAsync + AttachTaskAsync</c>。容器 Id 规则（R3）= <see cref="HistoryContainerRules"/>
    /// （声明面 Config\workflows\task-provision.ushar 的 `[Space] HistoryContainerRule` 对账单源）。
    /// 编排（建容器 → 归拢 task 的**顺序**）由声明流 `[Flow] TaskProvision` 承载（R2）；未装配流入口 ⇒ 迁移期回退直调。
    /// </summary>
    /// <param name="taskKey">任务键（= 会话名；按声明面规则作为历史容器 Id 的缺省回退来源）。</param>
    public async Task EnsureTaskHistoryBoundAsync(string taskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return;
        var hid = HistoryContainerRules.Resolve(null, taskKey);

        // R2 归流：编排真源 = task-provision.ushar 的 [Flow] TaskProvision（本处只是唯一触发点，不持顺序）。
        if (_taskProvisionFlow is not null)
        {
            await _taskProvisionFlow.ProvisionAsync(hid, hid, hid, ct).ConfigureAwait(false);
            return;
        }

        // 迁移期回退（未装配流入口时零行为变化）；退役条件见字段声明处。
        await EnsureHistoryAsync(hid, name: hid, ct).ConfigureAwait(false);
        await AttachTaskToHistoryAsync(hid, hid, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// task → node 关联（cross_db_link，真多对多；幂等）。历史节点集经此边派生（= 该历史全部任务节点并集），
    /// 是历史挂载节点的**唯一权威写路径**（替代已降级的 history→node 直写）。未装配关系守卫 → no-op。
    /// </summary>
    public Task AttachNodeToTaskAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _relations is null || string.IsNullOrWhiteSpace(taskKey) || string.IsNullOrWhiteSpace(nodeKey)
            ? Task.CompletedTask
            : _relations.LinkNodeAsync(taskKey.Trim(), workflowId, nodeKey, ct);

    /// <summary>
    /// 解除 task → node 关联（M:N 撤销边；幂等）。仅解除本 task 归属，不影响其它 task 共享引用。
    /// 与 <see cref="AttachNodeToTaskAsync"/> 成对（Link/Unlink）。未装配关系守卫 → no-op。
    /// </summary>
    public Task DetachNodeFromTaskAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _relations is null || string.IsNullOrWhiteSpace(taskKey) || string.IsNullOrWhiteSpace(nodeKey)
            ? Task.CompletedTask
            : _relations.UnlinkNodeAsync(taskKey.Trim(), workflowId, nodeKey, ct);

    /// <summary>归拢 task 到历史 + 关联 node 的组合原语（D4）：一次完成「确保历史归拢（EnsureTaskHistoryBoundAsync）
    /// + task→node 挂载（AttachNodeToTaskAsync）」，供「会话对话节点（conversation/addNode）」场景复用，
    /// 消除 UI 层 addNode 手写两步串；写边仍委托下层，本门面只编排。未装配关系守卫/空键 → no-op。</summary>
    public async Task AttachNodeToTaskBoundAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey) || string.IsNullOrWhiteSpace(nodeKey)) return;
        var key = taskKey.Trim();
        await EnsureTaskHistoryBoundAsync(key, ct).ConfigureAwait(false);
        await AttachNodeToTaskAsync(key, workflowId, nodeKey, ct).ConfigureAwait(false);
    }
}
