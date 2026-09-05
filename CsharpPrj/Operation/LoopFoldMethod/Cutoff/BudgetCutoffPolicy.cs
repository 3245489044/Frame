using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Cutoff;

/// <summary>预算截断策略：坐标达上限 → <see cref="FoldCut.BudgetExhausted"/>；否则继续。</summary>
/// <typeparam name="TState">状态点类型（未参与判定，仅契约占位）。</typeparam>
public sealed class BudgetCutoffPolicy<TState> : ICutoffPolicy<TState>
{
    public FoldCut Decide(TState head, int coordinate, FoldBudget budget)
        => budget.Exhausted(coordinate) ? FoldCut.BudgetExhausted : FoldCut.Continue;
}
