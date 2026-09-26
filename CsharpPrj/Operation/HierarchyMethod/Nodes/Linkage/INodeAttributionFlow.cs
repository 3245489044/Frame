using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 归属边簿记流入口（R1）—— `cross_db_link`（task → node）的**唯一编排触发点**。
/// <para>
/// 真源定位（《Agent 全量工作流化-编排归流纲领》§6.1 / §7 R1）：
/// 「何时建 / 什么入参 / 什么顺序」= 声明面 `Config/workflows/attribution.ushar` 的 `[Flow] NodeAttribution`；
/// 本接口只是该流的**调用入口**，不含任何编排语义 —— 实现体委托声明流执行（Agent 侧），
/// 使 Frame 侧调用点（<see cref="Execution.NodeStepExecutionService"/>）不反向依赖 Agent 程序集。
/// </para>
/// <para>
/// 与 <see cref="INodeTaskLinker"/> 的分工：后者是**批量原语门面**（内部逐点调写边守卫）；
/// 本接口是**编排入口**（一个 task×node 归属事实 = 一次流执行）。写边原语仍唯一归
/// <c>ReferenceGrounding.EnsureTaskNodeLinkAsync</c>（不复制写边逻辑）。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「迁移期回退 + 可见化」处理，不静默。</para>
/// </summary>
public interface INodeAttributionFlow
{
    /// <summary>把 node 挂到 task（`cross_db_link` 边；taskKey/nodeKey 空 ⇒ no-op，与写边守卫口径一致）。</summary>
    /// <param name="taskKey">任务键（会话键）。</param>
    /// <param name="workflowId">存储命名空间（节点 ThreadId；<b>非</b>工作流名）。</param>
    /// <param name="nodeKey">节点键。</param>
    /// <param name="ct">取消令牌。</param>
    Task AttachAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);
}
