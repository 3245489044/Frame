using System;

namespace Modules.Threading;

/// <summary>
/// 终止预算 —— 唯一的有界声明（总额 / 空闲窗口 / 单轮窗口）。
/// <para>传播规则：<see cref="Effective"/> 逐项取 min，禁止各层各自计时；
/// 绝对 deadline 由 <see cref="TerminationScope"/> 依 <see cref="ITimingClock"/> 单点换算。</para>
/// <para>null = 该维度不限（由其他维度/策略兜底），不表示 0。</para>
/// </summary>
public sealed record TerminationBudget(TimeSpan? Total = null, TimeSpan? Idle = null, TimeSpan? Round = null)
{
    /// <summary>全维度不限（仅协作取消 / 策略终止）。</summary>
    public static TerminationBudget Unbounded { get; } = new();

    /// <summary>与父预算合并：逐项 min（null 视为不限）。</summary>
    public TerminationBudget Effective(TerminationBudget? parent)
    {
        if (parent is null) return this;
        return new TerminationBudget(
            Min(Total, parent.Total),
            Min(Idle, parent.Idle),
            Min(Round, parent.Round));
    }

    private static TimeSpan? Min(TimeSpan? a, TimeSpan? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a <= b ? a : b;
    }
}
