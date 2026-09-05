namespace Operation.LoopFoldMethod.Operator;

/// <summary>
/// 算子单步产出：下一状态点 + 该步顶点（算子结论）。外部算子 <see cref="IFoldOperator{TState,TVertex}"/>
/// 的 Expand 返回载体 —— 「读当前状态点 → 展开出下一个状态点 → 追加到带」的最小闭环。
/// </summary>
/// <typeparam name="TState">状态点类型（类型化 FoldTrace 需同构带，故下一状态点与当前同型）。</typeparam>
/// <typeparam name="TVertex">该步算子结论类型。</typeparam>
public sealed record FoldStep<TState, TVertex>(TState Next, TVertex Vertex);
