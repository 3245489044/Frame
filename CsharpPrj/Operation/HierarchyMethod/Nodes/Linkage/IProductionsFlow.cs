using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 产出物化请求（富对象入参）—— 「本轮 AI 产出如何成为内容节点」的**调用面唯一载荷**：
/// 产出枚举 / 文本提取 / 序数分配（轮内到达序）一律由实现方（Agent 侧入口）完成，
/// 声明面只承载**步骤序**与字符串入参（O-1：编排真源 = 声明面）。
/// </summary>
/// <param name="Container">容器（会话名 / 任务键；身份键首段）。</param>
/// <param name="Round">轮次号（≤0 时由实现方归一为 1：0/负值会使跨轮同名覆盖回归）。</param>
/// <param name="Spec">本轮骨架（提供入参标记声明；null = 无声明 → 仅默认规则）。</param>
/// <param name="Nodes">本轮全量节点产出（null/空 ⇒ 合法 no-op）。</param>
/// <param name="TerminalText">终答文本（null = 无终答）。</param>
public sealed record ProductionsRequest(
    string Container,
    int Round,
    RoundSpec? Spec,
    IReadOnlyList<NodeExecutionRecord>? Nodes,
    string? TerminalText = null);

/// <summary>
/// 产出物化流入口（W2 / 纲领 §6.3）—— 回答「本轮 AI 产出如何成为内容节点」的**唯一编排触发点**。
/// <para>
/// 编排真源 = 声明面 <c>Config/workflows/session-productions.ushar</c> 的 <c>[Flow] SessionProductions</c>：
/// <c>accept_productions</c>（逐条归一 + 采用产出方 <c>ordinal</c> + 经身份模板生成 <c>identity_key</c>；
/// **不做角色过滤**，工具调用与终答同级入节点）→ <c>materialize_productions</c>（经唯一写门落库）。
/// 本接口只是该流的**调用入口**，不含顺序语义；实现体委托声明流执行（Agent 侧），
/// 使 Frame 侧调用点（<c>ConversationCentralPipeline</c>）不反向依赖 Agent 程序集。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「可见化 + 显式 ∅」处理，不静默。</para>
/// </summary>
public interface IProductionsFlow
{
    /// <summary>
    /// 受理并物化本轮产出：一次运行完成「逐条生成身份键 + 唯一写门落库」。
    /// </summary>
    /// <param name="request">产出物化请求（产出枚举 / 文本提取 / 序数分配由实现方完成）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true = 已提交或合法 no-op；false = 未装配执行器或流失败（调用方按可见化处理）。</returns>
    Task<FlowOutcome> AcceptAndMaterializeAsync(ProductionsRequest request, CancellationToken ct = default);
}
