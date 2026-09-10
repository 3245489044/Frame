using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Concurrency;

/// <summary>
/// UI 封送守卫（R3 线程隔离机制）—— 为 UI 封送（WinForms/WebView2/ECS 投影）提供**有界超时 + 失败隔离**：
/// 运行时后台线程调用 UI 时经本守卫包裹，超时即抛（归类为非致命封送失败），杜绝「同步上下文死锁 / 挂起 dispatcher 拖死运行时」
/// （D3/C4：sync-over-async + 5s 封送超时 ×3 + 60s 冻结的根治机制）。失败由调用方归类为非致命，不阻断主循环。
/// 与 <see cref="SyncAsyncBridge"/>（无同步上下文线程池执行，防 `.GetResult()` 死锁）配合：先经桥执行，再经本守卫限时。
/// </summary>
public static class UiDispatchGuard
{
    /// <summary>有界执行一次 UI 封送调用；超时抛 <see cref="TimeoutException"/>（隔离、非致命），并触发 <paramref name="onFail"/> 归因。</summary>
    public static async Task<T> InvokeAsync<T>(
        Func<Task<T>> uiCall,
        TimeSpan timeout,
        Action<string>? onFail = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uiCall);
        var effective = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effective);
        try
        {
            // 经 SyncAsyncBridge 无同步上下文执行，避免 UI 同步上下文阻塞；再限时等待结果。
            return await uiCall().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            onFail?.Invoke($"UI 封送等待超时（{effective.TotalSeconds}s）——已隔离，非致命。");
            throw new TimeoutException($"UI 封送等待超时（{effective.TotalSeconds}s），已隔离。");
        }
    }

    /// <summary>有界执行一次 UI 封送调用（无返回）。</summary>
    public static Task InvokeAsync(
        Func<Task> uiCall,
        TimeSpan timeout,
        Action<string>? onFail = null,
        CancellationToken ct = default)
        => InvokeAsync<object?>(async () => { await uiCall().ConfigureAwait(false); return null; }, timeout, onFail, ct);
}
