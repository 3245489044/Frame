using System;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Html;

namespace Modules.Observe.Transports;

/// <summary>
/// WebView2 传输适配器（HTML/UI 场景落地）：把 <see cref="IHtmlChannel"/>（纯渲染/封送通道）适配为 <see cref="ITransport"/>，
/// 使 HTML 写路径可经 <see cref="IObserveCoordinator"/> 获得「写-可观测」收敛保证。
/// 收敛证据：<see cref="EvidenceStrategy.Probe"/>——渲染投递后后台轮询 <see cref="IHtmlChannel.IsBridgeReadyAsync"/> 确认桥就绪（逼近式收敛），
/// 就绪即回发 <see cref="AckLevel.Converged"/>，界内未就绪则由协调器超时降级（I4）。
/// </summary>
public sealed class WebView2Transport : ITransport
{
    private static readonly TimeSpan DefaultConvergeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(50);

    private readonly IHtmlChannel _channel;
    private readonly TimeSpan _convergeTimeout;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private Func<Message, CancellationToken, Task>? _handler;
    private bool _disposed;

    public WebView2Transport(IHtmlChannel channel, TimeSpan? convergeTimeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _convergeTimeout = convergeTimeout ?? DefaultConvergeTimeout;
    }

    public EvidenceStrategy EvidenceStrategy => EvidenceStrategy.Probe;

    public IDisposable Subscribe(Func<Message, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock) _handler = handler;
        return new Unsubscriber(this);
    }

    public async Task SendAsync(Message message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (message is not ChangeCommand cmd)
            return;

        // B 已接纳渲染指令（Applied）：投递 HTML 到通道（唯一封送点）。
        var html = cmd.PayloadObject?.ToString() ?? string.Empty;
        await _channel.RenderHtmlAsync(html, ct).ConfigureAwait(false);
        Publish(new Ack(cmd.ChangeId, AckLevel.Applied, null));

        // 收敛确认异步进行（不阻塞投递返回）：桥就绪即回发 Converged，未就绪则由协调器超时降级。
        _ = ConfirmConvergenceAsync(cmd.ChangeId);
    }

    private async Task ConfirmConvergenceAsync(string changeId)
    {
        var deadline = DateTime.UtcNow + _convergeTimeout;
        while (DateTime.UtcNow < deadline && !_cts.IsCancellationRequested)
        {
            try
            {
                if (await _channel.IsBridgeReadyAsync(_cts.Token).ConfigureAwait(false))
                {
                    Publish(new Ack(changeId, AckLevel.Converged,
                        new ConvergedProof("webview2 dom bridge ready", DateTimeOffset.UtcNow)));
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 瞬时失败继续轮询（document 未就绪/导航中）
            }
            try { await Task.Delay(ProbeInterval, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Publish(Message message)
    {
        Func<Message, CancellationToken, Task>? h;
        lock (_lock) h = _handler;
        if (h is null) return;
        _ = Task.Run(async () =>
        {
            try { await h(message, _cts.Token).ConfigureAwait(false); }
            catch { /* 订阅方回调异常不向外抛 */ }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        lock (_lock) _handler = null;
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly WebView2Transport _owner;
        public Unsubscriber(WebView2Transport owner) => _owner = owner;
        public void Dispose()
        {
            lock (_owner._lock)
                _owner._handler = null;
        }
    }
}
