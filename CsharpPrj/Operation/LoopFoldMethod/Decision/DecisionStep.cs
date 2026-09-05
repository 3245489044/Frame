namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// 决策 skill 单步产出：下一状态点 + 该步结论 + 决策结果（下一步折叠链标识符 D′ 或终止信号）。
/// 与 <c>ISignalDrivenOperator</c> 的 <c>SignalFoldStep</c> 对齐，多携带 <see cref="DecisionOutcome"/>
/// （决策 = 产出标识符，而非独立的终态枚举）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">该步算子结论类型。</typeparam>
/// <param name="Next">下一状态点。</param>
/// <param name="Vertex">该步算子结论。</param>
/// <param name="Outcome">决策结果（下一步 D′ 或终止信号）。</param>
public sealed record DecisionStep<TState, TVertex>(
    TState Next,
    TVertex Vertex,
    DecisionOutcome Outcome);
