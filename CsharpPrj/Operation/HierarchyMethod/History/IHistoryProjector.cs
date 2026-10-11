using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Tasks;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史投影器（History 层）：把一轮编排结果沉淀到历史容器，并把某 task 累积历史投影为下次执行的上下文。
/// <para>
/// 分层口径：history 层只做「history → task 的集合管理」（history_owns_task 归拢，经 <see cref="IHistoryRelationManager"/>）；
/// task→node 归属（cross_db_link）属**节点执行层**，历史节点集为派生视图（= 其全部任务节点并集），
/// 本层不驱动节点归属（禁 history 直连 node）。
/// </para>
/// 「上下文组装」的终点 = <see cref="BuildContextAsync"/>：从历史重建 <see cref="NodeRoundInput"/>，
/// 交给投影器（Agent 的 provide_prompt 适配器）填 L0/L1/L2。框架本体零 LLM。未装配 → 消费方跳过沉淀（纯内存/DB-only 路径）。
/// </summary>
public interface IHistoryProjector
{
    /// <summary>轮次边界调用：把本轮的 history→task 归拢（history_owns_task）沉淀到历史容器。
    /// task→node 归属由节点执行层自持，不在本层处理。</summary>
    Task PersistRoundAsync(NodeRoundResult round, TaskScope scope, CancellationToken ct);

    // 入史投影（原 ProjectMarkedAsync）已退役（DEBT-4 残留收口）：唯一编排触发点 = 声明面
    // Config/workflows/round-attribution.ushar 的 [Flow] RoundAttribution（入口端口 IRoundAttributionFlow）。

    /// <summary>把某 task 累积历史投影为下一轮执行上下文（事务无关的只读重建）。</summary>
    Task<NodeRoundInput> BuildContextAsync(
        string taskKey, string? sessionId, CancellationToken ct);
}
