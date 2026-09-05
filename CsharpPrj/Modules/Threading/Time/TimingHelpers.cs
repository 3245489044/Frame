namespace Modules.Threading;

/// <summary>
/// 通用时间辅助：轮询等待（时钟驱动判定，fake 可测）与受取消延迟。
/// 注意：命名避开 <c>System.Threading.Timeout</c>，故称 <c>TimingHelpers</c>。
/// </summary>
public static class TimingHelpers
{
    /// <summary>轮询等待条件成立；预算耗尽返回 false。判定用时钟（可注入 FakeClock），等待用真实 Task.Delay。</summary>
    public static async Task<bool> PollUntilAsync(
        Func<bool> condition,
        TimeBudget budget,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default,
        ITimingClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "轮询周期必须 > 0");

        var c = clock ?? SystemClock.Instance;
        var deadline = budget.Deadline;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (condition())
                return true;
            if (c.UtcNow >= deadline)
                return false;

            try
            {
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>受取消令牌控制的延迟（生产等效 Task.Delay）。</summary>
    public static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        => Task.Delay(delay, cancellationToken);
}
