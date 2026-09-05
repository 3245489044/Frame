#nullable enable

using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrontEndDriven.Shared.Utils;

/// <summary>
/// UI 线程封送核心（单一权威实现）：BeginInvoke + TaskCompletionSource + 超时，防自锁（EL-4 语义）。
/// 消除 WinFormsUIDispatcher.Send 与 UiThreadHelper.DispatchIfNeeded 的同构重复与缺陷漂移
/// （两处曾各自复制"BeginInvoke + TCS + 5s 超时 + 异常回传"，修复只落一处漂移）。
/// 语义：目标为空/已在线程（InvokeRequired=false）→ 直接执行；跨线程 → 封送。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class UiDispatcherCore
{
    /// <summary>同步封送超时（毫秒），与历史语义一致（5s）。</summary>
    public const int SyncDispatchTimeoutMs = 5000;

    /// <summary>
    /// 在目标控件所属 UI 线程同步执行委托并返回结果（任一线程）。
    /// 已在线程/无目标 → 直接执行；否则 BeginInvoke + TCS + 超时。
    /// 超时抛 <see cref="TimeoutException"/>；委托异常原样回抛。
    /// </summary>
    public static TResult DispatchIfNeeded<TResult>(Control? target, Func<TResult> func)
    {
        if (func == null) throw new ArgumentNullException(nameof(func));

        if (target == null || !target.InvokeRequired)
            return func();

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        target.BeginInvoke(new Action(() =>
        {
            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }));

        if (!tcs.Task.Wait(TimeSpan.FromMilliseconds(SyncDispatchTimeoutMs)))
        {
            throw new TimeoutException(
                "UI 线程封送等待超时（5s）。若 UI 线程当前被同步阻塞（如 OnLoadAsync/GetAwaiter().GetResult()），" +
                "BeginInvoke 投递无法被执行，请使用异步调度路径。");
        }

        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>同步封送（无返回值）。</summary>
    public static void DispatchIfNeeded(Control? target, Action action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        DispatchIfNeeded(target, () => { action(); return true; });
    }

    /// <summary>
    /// 异步封送：跨线程 BeginInvoke + TCS（不阻塞调用线程），已在线程/无目标直接执行。
    /// 与同步版 <see cref="DispatchIfNeeded{TResult}(Control?, Func{TResult})"/> 对齐超时语义——
    /// UI 线程被同步阻塞时 TCS 永久挂起，超时抛 <see cref="TimeoutException"/>，避免调用方无限等待。
    /// </summary>
    public static async Task<TResult> DispatchIfNeededAsync<TResult>(Control? target, Func<TResult> func)
    {
        if (func == null) throw new ArgumentNullException(nameof(func));

        if (target == null || !target.InvokeRequired)
            return func();

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        target.BeginInvoke(new Action(() =>
        {
            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(SyncDispatchTimeoutMs))
            .ConfigureAwait(false);
        if (completed != tcs.Task)
        {
            throw new TimeoutException(
                $"UI 线程封送等待超时（{SyncDispatchTimeoutMs}ms）。若 UI 线程当前被同步阻塞（如 OnLoadAsync/GetAwaiter().GetResult()），" +
                "BeginInvoke 投递无法被执行，请改用异步调度路径。");
        }

        return await tcs.Task.ConfigureAwait(false);
    }
}
