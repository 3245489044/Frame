using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Observe;

/// <summary>
/// 探测式收敛传输（通用件，唯一实现）—— 「投递 → 回执 Applied → 后台轮询就绪谓词 → Converged / 超时降级」的
/// 传输生命周期单实现：任何「可观测就绪」的通道只需提供投递委托与就绪谓词，复用同一收敛证据语义
/// （<see cref="EvidenceStrategy.Probe"/>），不再各写一套轮询、回执与退订。
/// </summary>
public sealed class ProbeTransport : ITransport
{
    private static readonly TimeSpan DefaultConvergeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromMilliseconds(50);

    private readonly Func<ChangeCommand, CancellationToken, Task> _sendAsync;
    private readonly Func<CancellationToken, Task<bool>> _isConvergedAsync;
    private readonly TimeSpan _convergeTimeout;
    private readonly TimeSpan _probeInterval;
    private readonly string _proofLabel;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private Func<Message, CancellationToken, Task>? _handler;
    private bool _disposed;

    /// <param name="sendAsync">投递委托（已接纳的变更 → 落地到通道）。</param>
    /// <param name="isConvergedAsync">就绪谓词（true = 已可观测；瞬时异常视为未就绪继续轮询）。</param>
    /// <param name="convergeTimeout">收敛窗口（缺省 5s，界内未就绪即由协调器降级）。</param>
    /// <param name="probeInterval">轮询间隔（缺省 50ms）。</param>
    /// <param name="proofLabel">收敛证据标签（诊断可读）。</param>
    public ProbeTransport(
        Func<ChangeCommand, CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task<bool>> isConvergedAsync,
        TimeSpan? convergeTimeout = null,
        TimeSpan? probeInterval = null,
        string proofLabel = "channel ready")
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        _isConvergedAsync = isConvergedAsync ?? throw new ArgumentNullException(nameof(isConvergedAsync));
        _convergeTimeout = convergeTimeout ?? DefaultConvergeTimeout;
        _probeInterval = probeInterval ?? DefaultProbeInterval;
        _proofLabel = proofLabel ?? "channel ready";
    }

    /// <inheritdoc />
    public EvidenceStrategy EvidenceStrategy => EvidenceStrategy.Probe;

    /// <inheritdoc />
    public IDisposable Subscribe(Func<Message, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock) _handler = handler;
        return new Unsubscriber(this);
    }

    /// <inheritdoc />
    public async Task SendAsync(Message message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (message is not ChangeCommand cmd)
            return;

        // B 已接纳渲染指令（Applied）：投递到通道（唯一封送点）。
        await _sendAsync(cmd, ct).ConfigureAwait(false);
        Publish(new Ack(cmd.ChangeId, AckLevel.Applied, null));

        // 收敛确认异步进行（不阻塞投递返回）：就绪即回发 Converged，未就绪则由协调器超时降级。
        _ = ConfirmConvergenceAsync(cmd.ChangeId);
    }

    private async Task ConfirmConvergenceAsync(string changeId)
    {
        var deadline = DateTime.UtcNow + _convergeTimeout;
        while (DateTime.UtcNow < deadline && !_cts.IsCancellationRequested)
        {
            try
            {
                if (await _isConvergedAsync(_cts.Token).ConfigureAwait(false))
                {
                    Publish(new Ack(changeId, AckLevel.Converged,
                        new ConvergedProof(_proofLabel, DateTimeOffset.UtcNow)));
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
            try { await Task.Delay(_probeInterval, _cts.Token).ConfigureAwait(false); }
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

    /// <inheritdoc />
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
        private readonly ProbeTransport _owner;
        public Unsubscriber(ProbeTransport owner) => _owner = owner;
        public void Dispose()
        {
            lock (_owner._lock)
                _owner._handler = null;
        }
    }
}
