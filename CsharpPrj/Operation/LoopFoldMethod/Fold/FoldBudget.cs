namespace Operation.LoopFoldMethod.Fold;

/// <summary>
/// 折叠预算（记账 + 执法）：坐标上限。折叠器逐点展开的边界，
/// 对「不可信外部算子」无限膨胀的强制执法层（预算耗尽 → <see cref="FoldCut.BudgetExhausted"/>）。
/// </summary>
public sealed record FoldBudget(int MaxCoordinate)
{
    /// <summary>无上限预算（仅由其他截断策略 / 取消终止）。</summary>
    public static FoldBudget Unlimited { get; } = new(int.MaxValue);

    /// <summary>是否已达坐标上限（预算耗尽）。坐标 = 已完成的展开步数。</summary>
    public bool Exhausted(int coordinate) => coordinate >= MaxCoordinate;
}
