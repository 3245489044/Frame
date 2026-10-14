#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// 回流链路自检探针（L0 控件适配层）——注入 <c>__loopback__</c> postMessage 并确认回到 C# 消息桥入口，
/// 直接证明「DOM postMessage → WebMessageReceived → 消息桥」闭环（不依赖可选诊断 sink）。
/// 自愈重试：装配时序/导航竞态导致的瞬时断裂自动重试恢复；过程失败归拢，终态失败整块上报（降噪不丢信号）。
/// </summary>
public sealed class LoopbackProbe
{
    private const int ProbeAttempts = 3;
    private const int ProbeDelayMs = 150;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IWebViewHost _host;
    private readonly UiThreadMarshaler _marshaler;
    private readonly Func<CancellationToken, Task> _ensureReady;
    private readonly DiagnosticPort _diagnostics = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _waiters = new();
    private long _seq;

    /// <param name="host">控件宿主端口。</param>
    /// <param name="marshaler">UI 线程封送器（探针整体封送 UI 线程）。</param>
    /// <param name="ensureReady">确保就绪（重试前从源头恢复消息桥订阅）。</param>
    /// <param name="diagnostics">诊断出口（未接线为空操作）。</param>
    public LoopbackProbe(
        IWebViewHost host,
        UiThreadMarshaler marshaler,
        Func<CancellationToken, Task> ensureReady,
        Action<string, string>? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _marshaler = marshaler ?? throw new ArgumentNullException(nameof(marshaler));
        _ensureReady = ensureReady ?? throw new ArgumentNullException(nameof(ensureReady));
        _diagnostics.Sink = diagnostics;
    }

    /// <summary>回流链路自检（封送 UI 线程）。true=回流正常；false=传输断裂（未订阅/实例错配/超时）。</summary>
    public Task<bool> ProbeAsync(CancellationToken ct = default)
        => _marshaler.RunAsync(c => new ValueTask<bool>(ProbeCoreAsync(c)), ct);

    /// <summary>消费 <c>__loopback__</c> 探针负载（命中等待者 → 完成并返回 true，不冒泡到消息桥/外部事件）。</summary>
    public bool TryConsume(IReadOnlyDictionary<string, string>? payload)
    {
        if (payload is not null
            && payload.TryGetValue("cmd", out var cmd) && cmd == "__loopback__"
            && payload.TryGetValue("nonce", out var nonce)
            && _waiters.TryGetValue(nonce, out var tcs))
        {
            tcs.TrySetResult(nonce);
            return true;
        }
        return false;
    }

    private async Task<bool> ProbeCoreAsync(CancellationToken ct)
    {
        // 自愈重试：每次尝试前经 ensureReady（首次 / 重建后重新订阅消息桥），从源头消除「postMessage 已发出、C# 订阅丢失」。
        // 降噪：各次瞬态失败先归拢本地——自愈成功属预期路径；仅终态失败时把归拢的失败细节整块上报。
        var transientNotes = new List<string>();
        for (var attempt = 1; attempt <= ProbeAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _ensureReady(ct).ConfigureAwait(false);
                if (_host.Core is null)
                {
                    if (attempt < ProbeAttempts)
                    {
                        await Task.Delay(ProbeDelayMs, ct).ConfigureAwait(false);
                        continue;
                    }
                    break; // 终态仍无 CoreWebView2 → 失败（下方统一上报）
                }

                var nonce = "lb-" + Interlocked.Increment(ref _seq) + "-" + Guid.NewGuid().ToString("N");
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters[nonce] = tcs;
                try
                {
                    var js = "window.chrome.webview.postMessage(JSON.stringify({cmd:'__loopback__',nonce:'" + nonce + "'}));";
                    var exec = await _host.Core.ExecuteScriptAsync(js).ConfigureAwait(false);
                    using var timeout = new CancellationTokenSource(ProbeTimeout);
                    var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token)).ConfigureAwait(false);
                    if (done == tcs.Task && tcs.Task.IsCompletedSuccessfully)
                        return true; // 成功：瞬时失败不占行，终态确认由调用方统一输出
                    transientNotes.Add($"回流自检尝试 {attempt}/{ProbeAttempts} 未达 C# 消息桥（ExecuteScriptAsync 返回={TextUtil.Truncate(exec ?? string.Empty)}）");
                }
                finally
                {
                    _waiters.TryRemove(nonce, out _);
                }
            }
            catch (Exception ex)
            {
                if (attempt == ProbeAttempts) break; // 终态异常 → 失败（下方统一上报）
                transientNotes.Add($"回流自检异常 {attempt}/{ProbeAttempts}: {ex.GetType().Name}: {ex.Message}");
            }

            if (attempt < ProbeAttempts)
                await Task.Delay(ProbeDelayMs, ct).ConfigureAwait(false);
        }

        // 终态失败：把各次瞬态失败整块上报（保留失败定位所需细节）；外层自愈重试成功的预期路径不丢信号（明细走 transient 通道）。
        foreach (var note in transientNotes)
            _diagnostics.Report("HtmlChannel_loopback_transient", note);
        return false;
    }
}
