#nullable enable

using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrontEndDriven.Shared.Utils;
using TConfig.Abstractions;

namespace FrontEndDriven.Services;

public sealed class WinFormsUIDispatcher : IUIDispatcher
{
    private readonly Control _pumpControl;
    private readonly Func<bool> _hostAlive;
    private int _droppedAfterHostClosed;

    public WinFormsUIDispatcher(Control? pumpControl = null, Func<bool>? hostAlive = null)
    {
        if (pumpControl is null)
        {
            _pumpControl = new Control();
            _pumpControl.CreateControl();
        }
        else
        {
            _pumpControl = pumpControl;
        }
        _hostAlive = hostAlive ?? (static () => true);
    }

    public bool IsUIThread =>
        SynchronizationContext.Current is WindowsFormsSynchronizationContext;

    /// <summary>IEF+ C3：宿主已关闭/控件已释放导致被丢弃的投递计数（可见化；关窗后 0 异常）。</summary>
    public int DroppedAfterHostClosed => System.Threading.Volatile.Read(ref _droppedAfterHostClosed);

    /// <summary>
    /// IEF+ C3：栅栏投递——仅当 <c>HostAlive ∧ IsHandleCreated ∧ !IsDisposed</c> 才封送；不可达 ⇒
    /// 计数 + 丢弃（不抛 <c>BeginInvoke</c> 异常），关窗后事件投递不再制造 ObjectDisposedException。
    /// </summary>
    public bool TryPost(Action action)
    {
        if (action == null) return false;

        if (!_hostAlive() || _pumpControl.IsDisposed || !_pumpControl.IsHandleCreated)
        {
            System.Threading.Interlocked.Increment(ref _droppedAfterHostClosed);
            return false;
        }

        if (IsUIThread)
        {
            action();
            return true;
        }

        _pumpControl.BeginInvoke(action);
        return true;
    }

    public void Post(Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        TryPost(action); // 兼容入口：内部走栅栏（关窗后静默丢弃，不再抛 BeginInvoke 异常）
    }

    public void Send(Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (IsUIThread)
        {
            action();
            return;
        }

        // 统一封送核心（UiDispatcherCore，与 UiThreadHelper.DispatchIfNeeded 同一权威，消除缺陷漂移）：
        // 不再使用 _pumpControl.Invoke（同步阻塞死锁），改用 BeginInvoke + TCS + 5s 超时；保持同步返回语义，
        // 避免 UI 线程被阻塞时挂起调用线程。委托异常由核心原样回抛（InvalidOperationException 包装由调用方/适配层承担）。
        UiDispatcherCore.DispatchIfNeeded(_pumpControl, action);
    }

    public async Task InvokeAsync(Func<Task> asyncFunc)
    {
        if (asyncFunc == null)
            throw new ArgumentNullException(nameof(asyncFunc));

        // 统一走带栅栏/超时的异步封送（与 TryPost/Send 同一存活判定，消除关窗后永久挂起）。
        await InvokeAsync(async () => { await asyncFunc(); return true; }).ConfigureAwait(false);
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc)
    {
        if (asyncFunc == null)
            throw new ArgumentNullException(nameof(asyncFunc));

        if (IsUIThread)
            return await asyncFunc();

        if (!HostAcceptsDispatch())
            return default!;

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _pumpControl.BeginInvoke((Action)(async () =>
            {
                try { tcs.TrySetResult(await asyncFunc()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            System.Threading.Interlocked.Increment(ref _droppedAfterHostClosed);
            return default!;
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(UiDispatcherCore.SyncDispatchTimeoutMs))
            .ConfigureAwait(false);
        if (completed != tcs.Task)
            throw new TimeoutException(
                $"UI 线程异步封送等待超时（{UiDispatcherCore.SyncDispatchTimeoutMs}ms）：UI 线程可能被同步阻塞。");

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>投递栅栏：宿主存活 ∧ 控件未释放 ∧ 句柄已创建；不可达则计数并丢弃（与 <see cref="TryPost"/> 同口径）。</summary>
    private bool HostAcceptsDispatch()
    {
        if (!_hostAlive() || _pumpControl.IsDisposed || !_pumpControl.IsHandleCreated)
        {
            System.Threading.Interlocked.Increment(ref _droppedAfterHostClosed);
            return false;
        }
        return true;
    }

    public void AssertUIThread()
    {
        if (!IsUIThread)
            throw new InvalidOperationException(
                "当前操作必须在 UI 线程上执行。请通过 IUIDispatcher.InvokeAsync 封送。");
    }
}