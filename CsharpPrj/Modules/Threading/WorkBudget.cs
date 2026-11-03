using System;
using System.Diagnostics;

namespace Modules.Threading;

/// <summary>
/// 每帧工作量预算：以单调时钟计时，调用方在批次之间检查 <see cref="Exhausted"/> 决定是否把剩余工作留到下一帧。
/// <para>纯调度概念（线程/帧），不认体素、网格或渲染；替代散落在各处的 <c>Stopwatch.GetTimestamp()</c> 手工比较。</para>
/// </summary>
public sealed class WorkBudget
{
    private readonly long _startTicks;
    private readonly long _limitTicks;
    private readonly double _limitMs;
    private readonly bool _unlimited;

    private WorkBudget(double limitMs, bool unlimited)
    {
        _limitMs = limitMs;
        _unlimited = unlimited;
        _startTicks = Stopwatch.GetTimestamp();
        _limitTicks = unlimited
            ? long.MaxValue
            : _startTicks + (long)(Math.Max(0.0, limitMs) * Stopwatch.Frequency / 1000.0);
    }

    /// <summary>开始一份毫秒预算；≤ 0 视为「本帧不再做新工作」。</summary>
    public static WorkBudget Begin(double milliseconds)
        => new(milliseconds, unlimited: double.IsPositiveInfinity(milliseconds));

    /// <summary>不限量预算（诊断/一次性构建用）。</summary>
    public static WorkBudget Unlimited => new(double.PositiveInfinity, unlimited: true);

    public double LimitMs => _limitMs;

    public double ElapsedMs => _unlimited
        ? 0.0
        : (Stopwatch.GetTimestamp() - _startTicks) * 1000.0 / Stopwatch.Frequency;

    public double RemainingMs => _unlimited
        ? double.PositiveInfinity
        : Math.Max(0.0, _limitMs - ElapsedMs);

    /// <summary>预算是否已用尽（剩余 ≤ 0）。</summary>
    public bool Exhausted => !_unlimited && Stopwatch.GetTimestamp() >= _limitTicks;
}
