using System;
using System.Threading.Tasks;

namespace Share.HostAssembly.Concurrency;

/// <summary>
/// 同步-异步桥（单一实现）—— 在无同步上下文的线程池上执行异步动作再阻塞等待，
/// 避免 WinForms/同步上下文环境下 `.GetResult()` 同步等待导致的死锁风险。
/// 供同步释放（IDisposable）与「调用方只能同步等待」的桥接场景复用。
/// </summary>
public static class SyncAsyncBridge
{
    /// <summary>同步执行一个异步动作（无返回），阻塞直至完成。</summary>
    public static void Wait(Func<ValueTask> asyncAction)
    {
        ArgumentNullException.ThrowIfNull(asyncAction);
        Task.Run(async () => await asyncAction().ConfigureAwait(false)).GetAwaiter().GetResult();
    }

    /// <summary>同步执行一个异步动作（有返回），阻塞直至完成并返回结果。</summary>
    public static T WaitResult<T>(Func<Task<T>> asyncAction)
    {
        ArgumentNullException.ThrowIfNull(asyncAction);
        return Task.Run(async () => await asyncAction().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
}
