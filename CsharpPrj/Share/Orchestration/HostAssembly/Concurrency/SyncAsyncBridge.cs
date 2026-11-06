using System;
using System.Threading.Tasks;

namespace Share.HostAssembly.Concurrency;

/// <summary>
/// 同步-异步桥（单一实现）—— 在无同步上下文的线程池上执行异步动作再阻塞等待，
/// 避免 WinForms/同步上下文环境下 `.GetResult()` 同步等待导致的死锁风险。
/// 供同步释放（IDisposable）与「调用方只能同步等待」的桥接场景复用。
/// <para>
/// 有界重载（A-2/A-3 统一）：超时后抛 <see cref="TimeoutException"/>，被遗弃的任务继续在后台运行；
/// 调用方必须按「已放弃」处理——不得依赖其后续结果/回写，并在超时后自行落失败终态。
/// </para>
/// </summary>
public static class SyncAsyncBridge
{
    /// <summary>同步执行一个异步动作（无返回），阻塞直至完成。</summary>
    public static void Wait(Func<ValueTask> asyncAction)
    {
        PortGuard.Required(asyncAction, nameof(asyncAction));
        Task.Run(async () => await asyncAction().ConfigureAwait(false)).GetAwaiter().GetResult();
    }

    /// <summary>同步执行一个异步动作（有返回），阻塞直至完成并返回结果。</summary>
    public static T WaitResult<T>(Func<Task<T>> asyncAction)
    {
        PortGuard.Required(asyncAction, nameof(asyncAction));
        return Task.Run(async () => await asyncAction().ConfigureAwait(false)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 有界执行（无返回）：在 <paramref name="timeout"/> 内完成则正常返回；否则抛
    /// <see cref="TimeoutException"/>（任务被放弃、仍在后台运行，调用方须落失败终态）。
    /// <paramref name="timeout"/> ≤ <see cref="TimeSpan.Zero"/> 或 <see cref="Timeout.InfiniteTimeSpan"/> → 等价于无界 <see cref="Wait"/>。
    /// </summary>
    public static void WaitBounded(Func<Task> asyncAction, TimeSpan timeout)
    {
        PortGuard.Required(asyncAction, nameof(asyncAction));

        var task = Task.Run(async () => await asyncAction().ConfigureAwait(false));
        if (IsUnbounded(timeout))
        {
            task.GetAwaiter().GetResult();
            return;
        }

        task.WaitAsync(timeout).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 有界执行（有返回）：超时抛 <see cref="TimeoutException"/>；任务被放弃（未取消），
    /// 迟到结果不得被本次调用消费。
    /// </summary>
    public static T WaitResultBounded<T>(Func<Task<T>> asyncAction, TimeSpan timeout)
    {
        PortGuard.Required(asyncAction, nameof(asyncAction));

        var task = Task.Run(async () => await asyncAction().ConfigureAwait(false));
        if (IsUnbounded(timeout))
            return task.GetAwaiter().GetResult();

        return task.WaitAsync(timeout).GetAwaiter().GetResult();
    }

    /// <summary>有界执行（有返回，不抛超时）：true = 在窗口内完成（<paramref name="result"/> 有效）；false = 超时放弃。</summary>
    public static bool TryWaitResultBounded<T>(Func<Task<T>> asyncAction, TimeSpan timeout, out T result)
    {
        PortGuard.Required(asyncAction, nameof(asyncAction));

        var task = Task.Run(async () => await asyncAction().ConfigureAwait(false));
        try
        {
            result = IsUnbounded(timeout)
                ? task.GetAwaiter().GetResult()
                : task.WaitAsync(timeout).GetAwaiter().GetResult();
            return true;
        }
        catch (TimeoutException)
        {
            result = default!;
            return false;
        }
    }

    /// <summary>≤0 或 Infinite 视为「不设上界」（与既有调用点 timeout&lt;=0 表示无限一致）。</summary>
    public static bool IsUnbounded(TimeSpan timeout)
        => timeout == Timeout.InfiniteTimeSpan || timeout <= TimeSpan.Zero;
}
