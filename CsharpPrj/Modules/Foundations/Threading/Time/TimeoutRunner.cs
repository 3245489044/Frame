namespace Modules.Threading;

/// <summary>
/// 统一"超时运行"：以 <see cref="TimeBudget"/> 的单一 deadline 约束异步委托，
/// 返回 <see cref="TimeoutResult{T}"/> 并做**取消归因**（预算耗尽 vs 外部取消）。
/// 收编各处重复的 Task.WhenAny 竞速 / CancelAfter / 手写超时实现。
/// </summary>
public static class TimeoutRunner
{
    /// <summary>带预算运行异步委托；不抛出超时/取消（以 Outcome 表达）。</summary>
    /// <param name="name">操作名（用于异常信息）。</param>
    /// <param name="work">要执行的委托（应响应取消令牌）。</param>
    /// <param name="budget">单一 deadline 预算。</param>
    /// <param name="cancellationToken">外部取消令牌（与预算联动，优先归因外部取消）。</param>
    /// <param name="clock">可选时钟（默认 SystemClock）。</param>
    public static async Task<TimeoutResult<T>> RunAsync<T>(
        string name,
        Func<CancellationToken, Task<T>> work,
        TimeBudget budget,
        CancellationToken cancellationToken = default,
        ITimingClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        var c = clock ?? SystemClock.Instance;
        var started = c.UtcNow;

        // 预算到期经内部取消触发；外部令牌与之链接（谁先到由归因判定区分）。
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = budget.Remaining(c);
        if (remaining > TimeSpan.Zero)
            cts.CancelAfter(remaining);

        try
        {
            var value = await work(cts.Token).ConfigureAwait(false);
            return new TimeoutResult<T>(TimeoutOutcome.Completed, value, null, c.UtcNow - started);
        }
        catch (OperationCanceledException)
        {
            var elapsed = c.UtcNow - started;
            // 外部取消优先于预算耗尽（修正归因：不把外部取消的锅扣到框架执行层）。
            if (cancellationToken.IsCancellationRequested)
                return new TimeoutResult<T>(TimeoutOutcome.Cancelled, default, null, elapsed);
            return new TimeoutResult<T>(
                TimeoutOutcome.TimedOut,
                default,
                new TimeoutException($"{name} 超过预算 {budget.Total}"),
                elapsed);
        }
        catch (Exception ex)
        {
            return new TimeoutResult<T>(TimeoutOutcome.Failed, default, ex, c.UtcNow - started);
        }
    }
}
