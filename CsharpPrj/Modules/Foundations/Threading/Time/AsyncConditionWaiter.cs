namespace Modules.Threading;

/// <summary>
/// 异步条件轮询等待器：在统一预算（<see cref="TimeBudget"/>）与注入时钟（<see cref="ITimingClock"/>）下
/// 反复检查条件，直到条件成立、预算耗尽或取消。
/// <para>
/// 纪律：预算算术只经 <see cref="TimeBudget"/>，不得在调用点另写 <c>while + Stopwatch</c> 或固定次数循环；
/// 单次延时不超过剩余预算，保证总耗时不超过传入预算。
/// </para>
/// </summary>
public static class AsyncConditionWaiter
{
    /// <summary>默认轮询间隔。</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// 等待条件成立。
    /// </summary>
    /// <param name="condition">同步条件检查（应无阻塞、无副作用）。</param>
    /// <param name="total">总预算（&gt;0）。</param>
    /// <param name="clock">时间源；null 使用 <see cref="SystemClock.Instance"/>。</param>
    /// <param name="ct">取消令牌；取消时抛出 <see cref="OperationCanceledException"/>。</param>
    /// <param name="pollInterval">轮询间隔；null 使用 <see cref="DefaultPollInterval"/>，必须 &gt;0。</param>
    /// <returns>条件成立返回 true；预算耗尽返回 false。</returns>
    public static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan total,
        ITimingClock? clock = null,
        CancellationToken ct = default,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var interval = pollInterval ?? DefaultPollInterval;
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "轮询间隔必须 > 0");

        if (total <= TimeSpan.Zero)
            return condition();

        var budget = TimeBudget.From(total, margin: null, clock);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (condition()) return true;

            var remaining = budget.Remaining(clock);
            if (remaining <= TimeSpan.Zero) return false;

            var delay = remaining < interval ? remaining : interval;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等待异步条件成立（条件自身含 IO 时使用）。单次条件评估的耗时计入总预算。
    /// </summary>
    public static async Task<bool> WaitUntilAsync(
        Func<CancellationToken, Task<bool>> condition,
        TimeSpan total,
        ITimingClock? clock = null,
        CancellationToken ct = default,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var interval = pollInterval ?? DefaultPollInterval;
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "轮询间隔必须 > 0");

        if (total <= TimeSpan.Zero)
            return await condition(ct).ConfigureAwait(false);

        var budget = TimeBudget.From(total, margin: null, clock);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await condition(ct).ConfigureAwait(false)) return true;

            var remaining = budget.Remaining(clock);
            if (remaining <= TimeSpan.Zero) return false;

            var delay = remaining < interval ? remaining : interval;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }
}
