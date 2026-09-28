using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 上下文投影流入口（S4）—— 「nodes → task → history 的读取与组装」的**唯一编排触发点**。
/// <para>
/// 真源定位（《节点到任务到历史-组装归流与收集端口DSL扩展-重构方案》§4.2）：
/// 编排（步骤序 / 入参）= 声明面 <c>Config/workflows/context-projection.ushar</c> 的 <c>[Flow] ContextProject</c>；
/// 执行（单步只算）= <c>fn=context_project</c>；读源（本作用域节点输出）= 收集式输入端口 <c>inports=nodes.*</c>。
/// 本接口只是该流的**调用入口**，不含任何编排语义 —— 实现体委托声明流执行（Agent 侧），
/// 使调用方（读投影消费点）不反向依赖 Agent 程序集。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「回退旧读面 + 可见化」处理，不静默。</para>
/// </summary>
public interface IContextProjectionFlow
{
    /// <summary>跑一次读投影流（单作用域）；产物经 <see cref="ContextProjectionOutcome.Ports"/> 交回（键 = outports= 名）。</summary>
    /// <param name="request">投影请求（作用域 / 键 / 选择面 / 键前缀 / 命名空间 / task 作用域）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<ContextProjectionOutcome> ProjectAsync(ContextProjectionRequest request, CancellationToken ct = default);
}
