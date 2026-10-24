#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// UI 线程封送器——把「线程亲和封送」从控件方法体收敛为单一组件：
/// ① 句柄已创建 → 以 <see cref="IUiThreadAffine.InvokeRequired"/> 判定（句柄在则该判据精确）：同线程直执行 / 跨线程 InvokeOnUiAsync；
/// ② 句柄未创建且仍在控件创建线程 → 直执行（该线程将成为句柄所属线程）；
/// ③ 句柄未创建且跨线程 → 等 <see cref="IUiThreadAffine.HandleCreated"/> 后在句柄线程执行（5s 超时）。
/// <para>判据不得依赖 <c>InvokeRequired</c> 的句柄前取值（WinForms 在该态恒返回 false），也不得回退任意
/// <c>SynchronizationContext</c>（捕获的上下文未必是 UI 线程上下文，会把动作投递到线程池 ⇒ WebView2 跨线程异常）。</para>
/// <para>不持有控件/WebView2 语义；构造须在控件创建线程（记录该线程 id 以覆盖句柄前同线程调用）。</para>
/// </summary>
public sealed class UiThreadMarshaler
{
    /// <summary>SynchronizationContext.Post 封送等待超时（毫秒，对齐 UiDispatcherCore 的 5s 语义）。</summary>
    private const int DispatchTimeoutMs = 5000;

    private readonly IUiThreadAffine _host;
    private readonly int _uiThreadId;

    // 封送排队探针（单源只读）：调用 → action 真正开始执行的等待时长。用于区分「UI 线程被占」与「V8 执行慢」。
    private static long _lastQueueWaitMs;
    private static long _peakQueueWaitMs;

    public UiThreadMarshaler(IUiThreadAffine host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>最近一次封送排队时长（毫秒）。</summary>
    public static long LastQueueWaitMs => Interlocked.Read(ref _lastQueueWaitMs);

    /// <summary>取走并清零峰值封送排队时长（毫秒）：调用方按「一次写回」为窗口统计。</summary>
    public static long TakePeakQueueWaitMs() => Interlocked.Exchange(ref _peakQueueWaitMs, 0);

    /// <summary>UI 线程封送执行（无返回值）：句柄亲和判据见类型注释。</summary>
    public async Task RunAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        var started = Environment.TickCount64;
        if (_host.IsHandleCreated)
        {
            if (!_host.InvokeRequired)
            {
                await action(ct).ConfigureAwait(false);
                return;
            }

            await _host.InvokeOnUiAsync(c => { MarkQueueWait(started); return action(c); }, ct).ConfigureAwait(false);
            return;
        }

        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            // 句柄前且仍在控件创建线程：该线程将成为句柄所属线程，直接执行安全。
            await action(ct).ConfigureAwait(false);
            return;
        }

        await RunWhenHandleReadyAsync(started, action, ct).ConfigureAwait(false);
    }

    /// <summary>UI 线程封送执行（带返回值）：句柄亲和判据见类型注释。</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        var started = Environment.TickCount64;
        if (_host.IsHandleCreated)
        {
            if (!_host.InvokeRequired)
                return await action(ct).ConfigureAwait(false);

            return await _host.InvokeOnUiAsync(c => { MarkQueueWait(started); return action(c); }, ct).ConfigureAwait(false);
        }

        if (Environment.CurrentManagedThreadId == _uiThreadId)
            return await action(ct).ConfigureAwait(false);

        return await RunWhenHandleReadyAsync(started, action, ct).ConfigureAwait(false);
    }

    /// <summary>句柄未创建且跨线程：等 <see cref="IUiThreadAffine.HandleCreated"/> 后在句柄所属线程执行（带 5s 超时）。</summary>
    private Task RunWhenHandleReadyAsync(long started, Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCreated(object? sender, EventArgs e)
        {
            _host.HandleCreated -= OnCreated;
            _ = ExecutePendingAsync(started, action, tcs, ct);
        }

        _host.HandleCreated += OnCreated;
        // 订阅与判据之间的竞态：订阅时句柄已创建 ⇒ 立即改走封送（HandleCreated 不会再来）。
        if (_host.IsHandleCreated)
        {
            _host.HandleCreated -= OnCreated;
            _ = ExecutePendingAsync(started, action, tcs, ct);
        }

        return RaceWithTimeoutAsync(tcs.Task, "控件句柄创建");
    }

    /// <summary>句柄未创建且跨线程（带返回值）：延后到句柄所属线程执行。</summary>
    private Task<T> RunWhenHandleReadyAsync<T>(long started, Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCreated(object? sender, EventArgs e)
        {
            _host.HandleCreated -= OnCreated;
            _ = ExecutePendingAsync(started, action, tcs, ct);
        }

        _host.HandleCreated += OnCreated;
        if (_host.IsHandleCreated)
        {
            _host.HandleCreated -= OnCreated;
            _ = ExecutePendingAsync(started, action, tcs, ct);
        }

        return RaceWithTimeoutAsync(tcs.Task, "控件句柄创建");
    }

    private async Task ExecutePendingAsync(
        long started, Func<CancellationToken, ValueTask> action, TaskCompletionSource tcs, CancellationToken ct)
    {
        try
        {
            MarkQueueWait(started);
            await action(ct).ConfigureAwait(false);
            tcs.TrySetResult();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    private async Task ExecutePendingAsync<T>(
        long started, Func<CancellationToken, ValueTask<T>> action, TaskCompletionSource<T> tcs, CancellationToken ct)
    {
        try
        {
            MarkQueueWait(started);
            tcs.TrySetResult(await action(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    /// <summary>记录封送排队时长（调用 → 真正执行），并维护峰值。</summary>
    private static void MarkQueueWait(long started)
    {
        var wait = Environment.TickCount64 - started;
        Interlocked.Exchange(ref _lastQueueWaitMs, wait);
        long current;
        while (wait > (current = Interlocked.Read(ref _peakQueueWaitMs)))
        {
            if (Interlocked.CompareExchange(ref _peakQueueWaitMs, wait, current) == current) break;
        }
    }

    private static async Task RaceWithTimeoutAsync(Task task, string stage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(DispatchTimeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException($"UI 线程封送等待超时（5s，{stage}）：UI 泵可能被同步阻塞，或控件句柄从未创建。");
        await task.ConfigureAwait(false);
    }

    private static async Task<T> RaceWithTimeoutAsync<T>(Task<T> task, string stage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(DispatchTimeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException($"UI 线程封送等待超时（5s，{stage}）：UI 泵可能被同步阻塞，或控件句柄从未创建。");
        return await task.ConfigureAwait(false);
    }
}
