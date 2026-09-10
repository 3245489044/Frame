namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 确定性核心（Budget / Token / Flag）对象推进同步 —— 单一实现（StateRootSingleSource / SingleImplementation）。
/// 服务于快照还原：经对象推进（Escalate/Advance/SetHalt…）把序列化值还原到新建的 <see cref="ILoopState"/>，
/// 避免私有 setter 直写，对齐快照序列化还原语义。
/// </summary>
public static class DeterministicCoreCopier
{
    /// <summary>把指定值同步到目标状态（Budget 等级 / Token 值域 / Flag halt）。快照还原唯一入口。</summary>
    public static void CopyFromValues(ILoopState to, int budgetLevel, int tokenValue, int tokenMax, bool halt)
    {
        while (to.Budget.Level < budgetLevel) to.Budget.Escalate();
        while (to.Budget.Level > budgetLevel) to.Budget.Deescalate();
        to.Token.Max = tokenMax;
        while (to.Token.Value < tokenValue) to.Token.Advance();
        while (to.Token.Value > tokenValue) to.Token.Retreat();
        if (halt) to.Flag.SetHalt(); else to.Flag.Clear();
    }
}

/// <summary>
/// 连续失败计数规则 —— 单一写入语义（对齐 FailureBudgetEscalation 单一裁决）：
/// Fail/ParseError → +1，Pass → 归零，其余保持。
/// D7 口径区分：Partial（未完成）不计入连续失败——其推进语义由 ProgressToken 承载，
/// 与 FailureBudget（错误类别升级）及本计数（仅硬失败）为三套刻意区分的推进口径，勿合并。
/// </summary>
public static class ContinuousFailureRule
{
    public static int Next(int current, EvalOutcome eval) => eval switch
    {
        EvalOutcome.Fail or EvalOutcome.ParseError => current + 1,
        EvalOutcome.Pass => 0,
        _ => current,
    };
}
