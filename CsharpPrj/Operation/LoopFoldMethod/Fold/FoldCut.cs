namespace Operation.LoopFoldMethod.Fold;

/// <summary>
/// 折叠截断值（FoldCut ∈ { Continue, Success, BudgetExhausted, TripTerminated, Cancelled }）。
/// 终值（Success/BudgetExhausted/TripTerminated/Cancelled）由折叠器强制停止展开；
/// 非终值（Continue）继续。信任反转的执法出口：折叠器不信任外部算子自停，截断由 <see cref="Cutoff.ICutoffPolicy{TState}"/> 裁决。
/// </summary>
public enum FoldCut
{
    None,

    /// <summary>继续展开（非终值）。</summary>
    Continue,

    /// <summary>收敛成功 —— 折叠头达成目标 / 前缀收敛（判定点达成）。</summary>
    Success,

    /// <summary>预算耗尽 —— 坐标达到 <see cref="FoldBudget.MaxCoordinate"/>。</summary>
    BudgetExhausted,

    /// <summary>熔断终止 —— 不可信算子失控 / 空值守卫（算子返回 null）触发。</summary>
    TripTerminated,

    /// <summary>取消 —— <see cref="System.Threading.CancellationToken"/> 触发。</summary>
    Cancelled,
}
