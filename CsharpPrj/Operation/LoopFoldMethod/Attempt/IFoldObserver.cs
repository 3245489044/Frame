using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Attempt;

/// <summary>
/// 折叠观测（可选，非必需）：顶点追加 / 状态点推进。供日志、遥测、外部观察者挂钩；null = 无观测。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public interface IFoldObserver<TState, TVertex>
{
    /// <summary>每步顶点追加后触发（含坐标与算子结论）。</summary>
    void OnVertex(FoldVertex<TVertex> vertex);

    /// <summary>状态带推进到新状态点后触发。</summary>
    void OnStateAdvanced(TState state);
}
