namespace Operation.LoopFoldMethod.Fold;

/// <summary>
/// 状态带上的一个点：时间/坐标 + 该点算子结论（外部算子 <see cref="Operator.IFoldOperator{TState,TVertex}"/> 每步产出）。
/// 与状态点（FoldTrace 的 TState）分离：顶点记录「该步算子结论」，状态带记录「点本身」。
/// </summary>
/// <typeparam name="TVertex">该步算子结论类型。</typeparam>
public sealed record FoldVertex<TVertex>(int Coordinate, TVertex Conclusion);
