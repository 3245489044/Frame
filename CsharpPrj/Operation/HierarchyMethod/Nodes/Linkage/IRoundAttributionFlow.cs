using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 轮末沉淀请求（富对象入参）—— 轮末沉淀的**调用面唯一载荷**：产出枚举 / 终答归因 / 入史标记判定
/// 一律由实现方（Agent 侧入口）完成，声明面只承载**步骤序**与字符串入参（O-1：编排真源 = 声明面）。
/// </summary>
/// <param name="TaskKey">会话/任务键（= 历史容器 Id 的缺省来源）。</param>
/// <param name="Round">轮次号（≤0 时由实现方归一为 1：0/负值会使跨轮同名覆盖回归）。</param>
/// <param name="WorkflowId">存储命名空间（节点 ThreadId；空 ⇒ 由实现方取默认命名空间）。</param>
/// <param name="Spec">本轮骨架（提供入参标记声明；null = 无声明 → 仅默认规则）。</param>
/// <param name="Nodes">本轮全量节点产出（null/空 ⇒ 归属边回退骨架派生）。</param>
/// <param name="TerminalText">终答文本（null = 无终答）。</param>
/// <param name="HistoryId">显式历史容器 Id（null ⇒ 按声明面规则回退 <paramref name="TaskKey"/>）。</param>
public sealed record RoundAttributionRequest(
    string TaskKey,
    int Round,
    string WorkflowId,
    RoundSpec? Spec,
    IReadOnlyList<NodeExecutionRecord>? Nodes,
    string? TerminalText = null,
    string? HistoryId = null);

/// <summary>
/// 轮末沉淀流入口（DEBT-4 / 纲领 §6.3）—— 回答「本轮产物如何进历史」的**唯一编排触发点**。
/// <para>
/// 编排真源 = 声明面 <c>Config/workflows/round-attribution.ushar</c> 的 <c>[Flow] RoundAttribution</c>：
/// <c>link_nodes</c>（本轮产出批量建 <c>cross_db_link</c> 归属边）→ <c>project_marked</c>
/// （标记节点物化为 <c>conversation_message</c> + <c>session_owns</c> 挂载）。
/// 本接口只是该流的**调用入口**，不含顺序语义；实现体委托声明流执行（Agent 侧），
/// 使 Frame 侧调用点（<c>ConversationPipeline</c> / <c>SyncRelationsBlock</c>）不反向依赖 Agent 程序集。
/// </para>
/// <para>
/// 与既有单点流的分工：<see cref="INodeAttributionFlow"/> 管**节点执行期即时归属**（一个 task×node 事实），
/// 本接口管**轮末批量沉淀**（一轮的产出集合 + 入史范围）；两者共用同一写边守卫，不复制写边逻辑。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「迁移期回退 + 可见化」处理，不静默。</para>
/// </summary>
public interface IRoundAttributionFlow
{
    /// <summary>
    /// 轮末沉淀：一次运行完成「批量归属边 + 标记节点入史」。
    /// </summary>
    /// <param name="request">轮末沉淀请求（产出枚举 / 终答归因由实现方完成）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true = 已提交或合法 no-op；false = 未装配执行器或流失败（ERR-7：调用方按可见化处理）。</returns>
    Task<FlowOutcome> SettleRoundAsync(RoundAttributionRequest request, CancellationToken ct = default);
}
