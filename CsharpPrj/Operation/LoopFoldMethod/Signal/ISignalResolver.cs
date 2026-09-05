using Operation.LoopFoldMethod.Operator;

namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 信号解析产出：下一步要执行的步骤键 + 下一状态 + 入参顶点。非空 = 继续推进（有下一步）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
/// <param name="ExecuteStepKey">下一步要执行的步骤键（如链节点键）。</param>
/// <param name="NextState">下一状态点。</param>
/// <param name="InputVertex">喂给下一步的入参顶点（可选，来自上一步结论）。</param>
public sealed record SignalDirective<TState, TVertex>(
    string ExecuteStepKey,
    TState NextState,
    TVertex? InputVertex = default);

/// <summary>
/// 信号解析端口：把「一条信号 + 当前状态」解析为「下一步展开指令」。LLM-free 纯推演（链推进/终止判定），
/// 不执行任何步骤 —— 具体步骤由 <see cref="ISignalDrivenOperator{TState,TVertex}"/>（Agent 侧 SK 算子）承担。
/// 返回 null = 无下一步（链终点 / 非法信号 / 终止）→ 步进循环收敛（run_to_end）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public interface ISignalResolver<TState, TVertex>
{
    /// <summary>解析信号 → 下一步展开指令；null = 无下一步（终止）。</summary>
    /// <param name="signal">当前驱动信号。</param>
    /// <param name="state">当前状态点（只读）。</param>
    /// <param name="ctx">折叠上下文（坐标 + 预算 + 前置顶点，只读）。</param>
    SignalDirective<TState, TVertex>? Resolve(FoldSignal<TVertex> signal, TState state, FoldContext<TVertex> ctx);
}
