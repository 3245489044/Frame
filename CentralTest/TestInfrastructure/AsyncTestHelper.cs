using System.Diagnostics;

namespace TestInfrastructure;

/// <summary>
/// 异步测试辅助：为任意 <see cref="Task"/> 提供代码内超时护栏，避免死循环/死锁测试无限挂死 testhost。
/// 与 MSTest <c>[Timeout]</c> 形成双保险——<c>[Timeout]</c> 是宿主级整测试超时，
/// 本类可在测试体内对每个 await 点施加精确超时（失败即抛 <see cref="TimeoutException"/>）。
/// </summary>
public static class AsyncTestHelper
{
    /// <summary>等待任务在超时内完成；超时则抛 <see cref="TimeoutException"/>。</summary>
    public static async Task<T> WithTimeoutAsync<T>(Task<T> task, int timeoutMs, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException($"{description ?? "操作"}在 {timeoutMs}ms 内未完成");
        return await task.ConfigureAwait(false);
    }

    /// <summary>等待任务在超时内完成；超时则抛 <see cref="TimeoutException"/>。</summary>
    public static async Task WithTimeoutAsync(Task task, int timeoutMs, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException($"{description ?? "操作"}在 {timeoutMs}ms 内未完成");
        await task.ConfigureAwait(false);
    }

    /// <summary>
    /// 轮询等待条件成立直到超时；超时后最后再校验一次，仍不成立则抛 <see cref="TimeoutException"/>
    /// （避免"超时后误报成立"的语义歧义，见原分析报告 WaitUntil 超时语义问题）。
    /// </summary>
    public static async Task<bool> WaitUntilOrThrowAsync(
        Func<bool> condition,
        int timeoutMs = 5000,
        int pollIntervalMs = 50,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return true;
            await Task.Delay(pollIntervalMs).ConfigureAwait(false);
        }

        if (condition())
            return true;

        throw new TimeoutException($"{description ?? "条件"}在 {timeoutMs}ms 内未成立");
    }

    /// <summary>
    /// 异步谓词版轮询等待：谓词本身为异步（如需要 <c>await</c> 读取共享/持久化状态）。
    /// 语义与同步版一致——超时后最后再校验一次，仍不成立则抛 <see cref="TimeoutException"/>
    /// （避免"超时后误报成立"的语义歧义）。注意：超时即抛，不返回已读到的值；
    /// 若需在成立后读取终态值，应由调用方在谓词闭包里捕获。
    /// </summary>
    public static async Task WaitUntilOrThrowAsync(
        Func<Task<bool>> condition,
        int timeoutMs = 5000,
        int pollIntervalMs = 50,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (await condition().ConfigureAwait(false))
                return;
            await Task.Delay(pollIntervalMs).ConfigureAwait(false);
        }

        if (await condition().ConfigureAwait(false))
            return;

        throw new TimeoutException($"{description ?? "条件"}在 {timeoutMs}ms 内未成立");
    }
}
