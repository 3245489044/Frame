#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// UI 线程封送器——把「三路封送」从控件方法体收敛为单一组件：
/// ① 句柄已创建 → <see cref="IUiThreadAffine.InvokeOnUiAsync"/>；
/// ② 后台线程但句柄未创建（挂载前）→ 回退构造时捕获的 <see cref="SynchronizationContext.Post"/>（带 5s 超时兜底）；
/// ③ 已在 UI 线程 → 直接执行。
/// <para>不持有控件/WebView2 语义；构造须在控件创建线程（捕获该线程 SynchronizationContext）。</para>
/// </summary>
public sealed class UiThreadMarshaler
{
    /// <summary>SynchronizationContext.Post 封送等待超时（毫秒，对齐 UiDispatcherCore 的 5s 语义）。</summary>
    private const int DispatchTimeoutMs = 5000;

    private readonly IUiThreadAffine _host;
    private readonly SynchronizationContext? _uiSyncContext;

    public UiThreadMarshaler(IUiThreadAffine host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _uiSyncContext = SynchronizationContext.Current;
    }

    /// <summary>UI 线程封送执行（无返回值）。三路封送语义见类型注释。</summary>
    public async Task RunAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        if (_host.InvokeRequired && _host.IsHandleCreated)
            await _host.InvokeOnUiAsync(action, ct).ConfigureAwait(false);
        else if (_host.InvokeRequired && _uiSyncContext is not null)
            await InvokeOnSyncContextAsync(action, ct).ConfigureAwait(false);
        else
            await action(ct).ConfigureAwait(false);
    }

    /// <summary>UI 线程封送执行（带返回值）。三路封送语义见类型注释。</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        if (_host.InvokeRequired && _host.IsHandleCreated)
            return await _host.InvokeOnUiAsync(action, ct).ConfigureAwait(false);
        if (_host.InvokeRequired && _uiSyncContext is not null)
            return await InvokeOnSyncContextAsync(action, ct).ConfigureAwait(false);
        return await action(ct).ConfigureAwait(false);
    }

    private Task InvokeOnSyncContextAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = _uiSyncContext!;
        ctx.Post(async _ =>
        {
            try
            {
                await action(ct).ConfigureAwait(false);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return RaceWithTimeoutAsync(tcs.Task);
    }

    private Task<T> InvokeOnSyncContextAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = _uiSyncContext!;
        ctx.Post(async _ =>
        {
            try
            {
                var r = await action(ct).ConfigureAwait(false);
                tcs.TrySetResult(r);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return RaceWithTimeoutAsync(tcs.Task);
    }

    private static async Task RaceWithTimeoutAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(DispatchTimeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException("UI 线程封送回退（SynchronizationContext.Post）等待超时（5s）：UI 泵可能被同步阻塞。");
        await task.ConfigureAwait(false);
    }

    private static async Task<T> RaceWithTimeoutAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(DispatchTimeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException("UI 线程封送回退（SynchronizationContext.Post）等待超时（5s）：UI 泵可能被同步阻塞。");
        return await task.ConfigureAwait(false);
    }
}
