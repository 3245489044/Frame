using System;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 任务供给簿记流入口（R2）—— `history_owns_task`（history → task）的**唯一编排触发点**。
/// <para>
/// 真源定位（《Agent 全量工作流化-编排归流纲领》§6.2 / §7 R2）：
/// 「何时建 / 什么入参 / 什么顺序」= 声明面 `Config/workflows/task-provision.ushar` 的 `[Flow] TaskProvision`；
/// 本接口只是该流的**调用入口**，不含任何编排语义 —— 实现体委托声明流执行（Agent 侧），
/// 使 Frame 侧调用点（<see cref="Reference.NodeTaskHistoryService"/> / <see cref="Hierarchy.HierarchyManager"/> /
/// <see cref="History.HistoryProjector"/>）不反向依赖 Agent 程序集。
/// </para>
/// <para>
/// 与写边原语的分工：本接口是**编排入口**（一个「容器存在 + 归拢」事实 = 一次流执行）；
/// 写边原语仍唯一归 <c>ReferenceGrounding</c>（不复制写边逻辑），由流内 `fn=` 单步调用。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「迁移期回退 + 可见化」处理，不静默。</para>
/// </summary>
public interface ITaskProvisionFlow
{
    /// <summary>
    /// 确保历史容器存在并归拢 task（`history_owns_task` 边；historyId/taskKey 空 ⇒ 合法 no-op）。
    /// 容器 Id 规则（R3）由声明面承载，调用方只给候选值。
    /// </summary>
    /// <param name="historyId">历史容器 Id（显式优先；空 ⇒ 按声明面规则回退 taskKey）。</param>
    /// <param name="historyName">历史容器展示名（空 ⇒ 由流内规则取 historyId）。</param>
    /// <param name="taskKey">任务键（会话键）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true = 已提交或合法 no-op；false = **未装配执行器 / 流执行失败**（ERR-7：调用方可据此回退或可见化，禁静默）。</returns>
    Task<bool> ProvisionAsync(string? historyId, string? historyName, string taskKey, CancellationToken ct = default);
}
