namespace Modules.Threading;

/// <summary>
/// 单一 deadline 预算：统一时间管理的传播载体。
/// 取代各层写死限时常量的"赛跑"——以绝对 deadline 为唯一时钟，向下传"剩余时间"。
/// </summary>
public readonly struct TimeBudget
{
    /// <summary>预算总额（自创建起）。</summary>
    public TimeSpan Total { get; }

    /// <summary>安全余量（供内层自报超时，避免内层与框架 kill 竞速）。</summary>
    public TimeSpan Margin { get; }

    /// <summary>绝对截止时刻（唯一时钟）。</summary>
    public DateTimeOffset Deadline { get; }

    public TimeBudget(TimeSpan total, TimeSpan? margin, ITimingClock? clock)
    {
        if (total <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(total), "预算必须 > 0");
        Total = total;
        Margin = margin ?? TimeSpan.Zero;
        Deadline = (clock ?? SystemClock.Instance).UtcNow + total;
    }

    /// <summary>创建预算：total 自创建起计时，margin 为内层余量。</summary>
    public static TimeBudget From(TimeSpan total, TimeSpan? margin = null, ITimingClock? clock = null)
        => new(total, margin, clock);

    /// <summary>从已推导的剩余预算创建（若剩余已不足，Total 按传入值记录，Deadline 贴近当前）。</summary>
    public static TimeBudget FromRemaining(TimeSpan remaining, TimeSpan? margin = null, ITimingClock? clock = null)
        => new(remaining > TimeSpan.Zero ? remaining : throw new ArgumentOutOfRangeException(nameof(remaining), "剩余预算必须 > 0"), margin, clock);

    /// <summary>距离 deadline 的剩余时长（≤0 返回零）。</summary>
    public TimeSpan Remaining(ITimingClock? clock = null)
    {
        var r = Deadline - (clock ?? SystemClock.Instance).UtcNow;
        return r > TimeSpan.Zero ? r : TimeSpan.Zero;
    }

    /// <summary>剩余预算扣除 margin（供内层自报超时使用）。</summary>
    public TimeSpan RemainingAfterMargin(ITimingClock? clock = null)
    {
        var r = Remaining(clock) - Margin;
        return r > TimeSpan.Zero ? r : TimeSpan.Zero;
    }

    /// <summary>预算是否已耗尽。</summary>
    public bool IsExpired(ITimingClock? clock = null) => Remaining(clock) <= TimeSpan.Zero;
}
