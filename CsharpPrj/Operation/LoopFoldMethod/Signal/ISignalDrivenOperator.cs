using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Operator;

namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 信号驱动算子端口：消费一条信号 → 展开一步 → 产出 <see cref="SignalFoldStep{TState,TVertex}"/>。
/// 唯一外部执行面；框架不感知具体实现（LLM-free）—— Agent 侧以 Semantic Kernel（技能/工具/MCP）实现本端口。
/// 与 <see cref="Operator.IFoldOperator{TState,TVertex}"/> 的关系：本端口是信号驱动的算子形态，
/// 显式接收 <paramref name="signal"/> 与 <paramref name="stepKey"/>，用于「信号 → 一步」映射场景。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public interface ISignalDrivenOperator<TState, TVertex>
{
    /// <summary>
    /// 展开当前步：消费信号 → 执行本步（stepKey 定位）→ 返回下一状态 + 顶点 + 该步键。
    /// </summary>
    /// <param name="state">当前状态点（只读）。</param>
    /// <param name="signal">驱动信号（ExternalInput 首信号 / StepCompleted 链推进信号）。</param>
    /// <param name="stepKey">本步要执行的步骤键（由 <see cref="ISignalResolver{TState,TVertex}"/> 解析产出）。</param>
    /// <param name="ctx">折叠上下文（坐标 + 预算 + 前置顶点，只读）。</param>
    /// <param name="ct">取消令牌（算子应观察并在取消时抛 OperationCanceledException）。</param>
    Task<SignalFoldStep<TState, TVertex>> ExpandAsync(
        TState state, FoldSignal<TVertex> signal, string stepKey, FoldContext<TVertex> ctx, CancellationToken ct);
}
