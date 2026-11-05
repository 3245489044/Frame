using System.Diagnostics;

namespace Modules.Threading;

/// <summary>
/// 单调时钟抽象（可测）。所有时间控制经此读取"当前时刻"，取代散落的
/// <c>DateTime.UtcNow</c>/<c>Stopwatch</c>，使超时/空闲判定可注入 FakeClock 确定性测试。
/// </summary>
public interface ITimingClock
{
    /// <summary>当前绝对时刻（UTC）。</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>进程启动以来的单调毫秒数（相对计时，不回拨）。</summary>
    long ElapsedMilliseconds { get; }
}

/// <summary>生产时钟：UTC 时刻 + Stopwatch 单调毫秒。单例可共享。</summary>
public sealed class SystemClock : ITimingClock
{
    /// <summary>全局共享生产时钟实例。</summary>
    public static readonly SystemClock Instance = new();

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
}

/// <summary>测试时钟：可控推进（FakeClock）。时间相关判定均可由 <see cref="Advance"/> 驱动。</summary>
public sealed class FakeClock : ITimingClock
{
    private DateTimeOffset _utcNow;
    private long _elapsedMilliseconds;

    public FakeClock(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow => _utcNow;

    public long ElapsedMilliseconds => _elapsedMilliseconds;

    /// <summary>推进时钟一段时长。</summary>
    public void Advance(TimeSpan amount)
    {
        _utcNow += amount;
        _elapsedMilliseconds += (long)amount.TotalMilliseconds;
    }

    /// <summary>直接设定时刻（按相对差推进）。</summary>
    public void Set(DateTimeOffset target)
    {
        _elapsedMilliseconds += (long)(target - _utcNow).TotalMilliseconds;
        _utcNow = target;
    }
}
