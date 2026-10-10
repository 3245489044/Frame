using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 任务 / 历史生命周期流入口（T 批）—— «task 与 history 本身就是工作流»：
/// Create / Advance / Compose / Merge / Detach 五类操作各是一条声明 `[Flow]` 的**唯一调用入口**。
/// <para>
/// 真源定位（《USharp数据化-后续未实施清单与Task/History工作流化-设计》§2）：
/// 「何时建 / 什么入参 / 什么顺序」= 声明面 <c>Config/workflows/{task-lifecycle,task-advance,task-compose,history-merge,history-detach}.ushar</c>；
/// 本接口只是这些流的**调用入口**，不含任何编排语义 —— 实现体委托声明流执行（Agent 侧），
/// 使 Frame 侧调用点不反向依赖 Agent 程序集。
/// </para>
/// <para>
/// 与写边原语的分工：本接口是**编排入口**，写边原语仍唯一归 <c>ReferenceGrounding</c> /
/// <c>IHistoryRelationManager</c>（不复制写边逻辑），由流内 <c>fn=</c> 单步调用。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「可见化（禁静默）」处理。</para>
/// </summary>
public interface ITaskLifecycleFlow
{
    /// <summary>Create：建历史容器 + 归拢 task + 挂节点（`task-lifecycle.ushar` 的 [Flow] TaskLifecycle）。
    /// taskKey 空 ⇒ 合法 no-op（true）。</summary>
    /// <param name="historyId">历史容器 Id（显式优先；空 ⇒ 按声明面规则回退 taskKey）。</param>
    /// <param name="historyName">历史容器展示名（空 ⇒ 由流内规则取 historyId）。</param>
    /// <param name="taskKey">任务键（会话键）。</param>
    /// <param name="workflowId">存储命名空间（节点 ThreadId；空 ⇒ 流内归一为默认命名空间）。</param>
    /// <param name="nodeKeys">本轮归属节点键集（空 ⇒ 挂节点步合法 no-op）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true = 已提交或合法 no-op；false = 未装配执行器 / 流失败（可见化，禁静默）。</returns>
    Task<bool> CreateAsync(
        string? historyId, string? historyName, string taskKey, string? workflowId,
        IReadOnlyCollection<string>? nodeKeys, CancellationToken ct = default);

    /// <summary>Advance：task 状态 / 载荷推进（`task-advance.ushar` 的 [Flow] TaskAdvance；图端点簿记镜像）。
    /// taskKey / toStatus 空 ⇒ 合法 no-op（true）。</summary>
    Task<bool> AdvanceAsync(string taskKey, string toStatus, CancellationToken ct = default);

    /// <summary>Compose：父 task 归拢子 task（`task-compose.ushar` 的 [Flow] TaskCompose；`task_owns_task` 不定深度树 + 环检测）。
    /// 空键 / 自指 / 成环 ⇒ 合法 no-op（true，由写边原语判据拒绝）。</summary>
    Task<bool> ComposeAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default);

    /// <summary>Merge：历史容器合并（`history-merge.ushar` 的 [Flow] HistoryMerge；迁边先写后撤，幂等）。
    /// 空键 / 同容器 ⇒ 合法 no-op（true）。</summary>
    Task<bool> MergeHistoriesAsync(string intoHistory, string fromHistory, CancellationToken ct = default);

    /// <summary>Detach：解除 history→task 归拢（`history-detach.ushar` 的 [Flow] HistoryDetach；幂等）。
    /// 空键 ⇒ 合法 no-op（true）。</summary>
    Task<bool> DetachAsync(string historyId, string taskKey, CancellationToken ct = default);
}
