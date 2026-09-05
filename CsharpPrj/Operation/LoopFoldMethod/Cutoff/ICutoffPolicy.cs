using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Cutoff;

/// <summary>
/// 截断策略：预算 / 轮次 / 收敛 / 取消 → <see cref="FoldCut"/>。
/// 信任反转的执法层 —— 外部算子不可信、不会自觉终止，折叠器经本策略对「不可信外部算子」强制截断
/// （不信任算子自停，预算/轮次/收敛/取消任一路由到截断即停）。
/// </summary>
/// <typeparam name="TState">状态点类型（逆变，只读判定源）。</typeparam>
public interface ICutoffPolicy<in TState>
{
    /// <summary>判定是否截断并给出截断值（终值 = 停止；Continue = 继续）。</summary>
    /// <param name="head">当前折叠头（已展开前缀的判定点）。</param>
    /// <param name="coordinate">当前坐标（已完成的展开步数）。</param>
    /// <param name="budget">折叠预算（坐标上限）。</param>
    FoldCut Decide(TState head, int coordinate, FoldBudget budget);
}
