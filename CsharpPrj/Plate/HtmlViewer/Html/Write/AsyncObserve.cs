#nullable enable

using System;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// fire-and-forget 的**受观察**执行：把「无主 Task」的异常经 <c>onError</c> 上报，
/// 消除未观察 Task 异常（finalizer 重抛 → AppDomain 出口）并让写回失败可见（F2）。
/// </summary>
public static class AsyncObserve
{
    /// <summary>观察 <paramref name="task"/>；已成功则零开销返回（不分配续体）。</summary>
    public static void Run(Task task, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(onError);
        if (task.IsCompletedSuccessfully) return;
        _ = ObserveAsync(task, onError);
    }

    private static async Task ObserveAsync(Task task, Action<Exception> onError)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) { onError(ex); }
    }
}
