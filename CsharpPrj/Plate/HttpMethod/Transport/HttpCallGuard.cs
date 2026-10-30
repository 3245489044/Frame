using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.HttpMethod.Transport;

/// <summary>通用调用结果（成功值 / 超时 / 取消 / 异常四态；异常不抛出、折叠为错误文本）。</summary>
public readonly record struct HttpCallOutcome<T>(T? Value, bool IsSuccess, bool TimedOut, string? Error)
{
    public static HttpCallOutcome<T> Ok(T value) => new(value, true, false, null);

    public static HttpCallOutcome<T> Fail(string error) => new(default, false, false, error);
}

/// <summary>
/// 通用 HTTP 调用超时壳（SingleImplementation）：按调用名施加显式超时，把超时/异常折叠为结构化结果。
/// <para>连接池工厂不设硬编码单点超时（见 <see cref="HttpTransportFactory"/>），故超时机制统一收在这里，
/// 供各插件（Agent 网关、外部数据源等）复用，避免各自重写 <c>CancellationTokenSource.CancelAfter</c>。</para>
/// <para>语义：调用方取消（<paramref name="ct"/> 已触发）→ 原样抛出 <see cref="OperationCanceledException"/>；
/// 自身超时 → <see cref="HttpCallOutcome{T}.TimedOut"/>；其余异常 → 折叠为错误文本。</para>
/// </summary>
public static class HttpCallGuard
{
    /// <summary>执行一次带超时的调用。</summary>
    /// <param name="name">调用名（用于错误/超时文案归因）。</param>
    /// <param name="timeout">超时预算（须 &gt; 0）；到点取消在途调用。</param>
    /// <param name="call">实际调用（接收超时联动后的令牌）。</param>
    /// <param name="ct">调用方取消令牌（触发时原样抛出）。</param>
    public static async Task<HttpCallOutcome<T>> RunAsync<T>(
        string name,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> call,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(call);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "超时预算须大于 0");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try
        {
            var value = await call(linked.Token).ConfigureAwait(false);
            return HttpCallOutcome<T>.Ok(value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new HttpCallOutcome<T>(default, false, true,
                $"{name} 调用超时（>{timeout.TotalMilliseconds:0}ms）");
        }
        catch (Exception ex)
        {
            return HttpCallOutcome<T>.Fail($"{name} 调用异常: {ex.Message}");
        }
    }
}
