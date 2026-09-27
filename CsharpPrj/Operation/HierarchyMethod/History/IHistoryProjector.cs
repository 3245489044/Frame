using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Nodes.Workflows;
using Operation.HierarchyMethod.Tasks;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史投影器（History 层）：把一轮编排结果沉淀到历史容器，并把某 task 累积历史投影为下次执行的上下文。
/// 历史容器与引用边（history_owns_task / session_owns）经 <see cref="IHistoryRelationManager"/> 建（写边原语不复制）。
/// 「上下文组装」的终点 = <see cref="BuildContextAsync"/>：从历史重建 <see cref="NodeRoundInput"/>，
/// 交给投影器（Agent 的 provide_prompt 适配器）填 L0/L1/L2。Agent 侧以 Conversation 历史 + HistoryRelationManager 适配实现；
/// 框架本体零 LLM。未装配 → 消费方跳过沉淀（纯内存/DB-only 路径）。
/// </summary>
public interface IHistoryProjector
{
    /// <summary>每个节点/任务执行后调用：沉淀历史记录 + 引用边（history_owns_task / session_owns）。</summary>
    Task PersistRoundAsync(NodeRoundResult round, TaskScope scope, CancellationToken ct);

    /// <summary>
    /// 入史投影（CP-3，**唯一决定哪些节点进历史**）：按 <see cref="HistoryMarkPolicy"/> 过滤轮次产物，
    /// 把标记节点投影为 <c>NodeType=conversation_message</c> 节点（<c>session.{name}.msg.{seq}</c>，seq = 时序权威）
    /// + <c>session_owns</c> 挂载边；历史顺序 = 键内 <c>(round, seq)</c>（<c>round_sequence</c> 边已退役，A4′）。
    /// <para>未装配节点库/关系管理 → 各写路径跳过（不抛、不阻断）；调用方消费返回值判定是否命中。</para>
    /// </summary>
    /// <returns>实际投影的标记节点数（0 = 无标记节点或未装配）。</returns>
    Task<int> ProjectMarkedAsync(RoundArtifacts artifacts, RoundSpec? spec, TaskScope scope, CancellationToken ct);

    /// <summary>把某 task 累积历史投影为下一轮执行上下文（事务无关的只读重建）。</summary>
    Task<NodeRoundInput> BuildContextAsync(
        string taskKey, string? sessionId, CancellationToken ct);
}
