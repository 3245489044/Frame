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

    public WinFormsUIDispatcher()
    {
        _pumpControl = new Control();
        _pumpControl.CreateControl();
    }

    public bool IsUIThread =>
        SynchronizationContext.Current is WindowsFormsSynchronizationContext;

    public void Post(Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (IsUIThread)
        {
            action();
        }
        else
        {
            _pumpControl.BeginInvoke(action);
        }
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

        if (IsUIThread)
        {
            await asyncFunc();
        }
        else
        {
            var tcs = new TaskCompletionSource();
            _pumpControl.BeginInvoke((Action)(async () =>
            {
                try { await asyncFunc(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            }));
            await tcs.Task;
        }
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc)
    {
        if (asyncFunc == null)
            throw new ArgumentNullException(nameof(asyncFunc));

        if (IsUIThread)
        {
            return await asyncFunc();
        }

        var tcs = new TaskCompletionSource<T>();
        _pumpControl.BeginInvoke((Action)(async () =>
        {
            try { tcs.SetResult(await asyncFunc()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }));
        return await tcs.Task;
    }

    public void AssertUIThread()
    {
        if (!IsUIThread)
            throw new InvalidOperationException(
                "当前操作必须在 UI 线程上执行。请通过 IUIDispatcher.InvokeAsync 封送。");
    }
}