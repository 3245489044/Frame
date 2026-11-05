using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Observe;

/// <summary>
/// 内存自环传输（单测/无 UI 环境用）：收到 <see cref="ChangeCommand{T}"/> 后按 <c>Send*</c> 开关模拟 Consumer(B)
/// 回发三级 ack 链。声明 <see cref="EvidenceStrategy.RenderCallback"/>（ack 即回调式证据）。
/// </summary>
public sealed class InMemoryTransport : ITransport
{
    private readonly object _lock = new();
    private Func<Message, CancellationToken, Task>? _handler;
    private bool _disposed;

    /// <summary>总开关：false 时完全不回执（模拟挂起/黑盒运行时）。</summary>
    public bool Acknowledge { get; set; } = true;

    public bool SendAccepted { get; set; } = true;
    public bool SendApplied { get; set; } = true;
    public bool SendConverged { get; set; } = true;

    /// <summary>ack 链回发前的模拟延迟。</summary>
    public TimeSpan AckDelay { get; set; } = TimeSpan.Zero;

    public EvidenceStrategy EvidenceStrategy => EvidenceStrategy.RenderCallback;

    public IDisposable Subscribe(Func<Message, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            _handler = handler;
            return new Subscription(this);
        }
    }

    public async Task SendAsync(Message message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 模拟 Consumer(B)：收到变更命令后按开关回发 ack 链到订阅方（生产者的协调器）。
        if (message is ChangeCommand cmd && Acknowledge)
        {
            if (AckDelay > TimeSpan.Zero)
                await Task.Delay(AckDelay, ct).ConfigureAwait(false);
            Func<Message, CancellationToken, Task>? h;
            lock (_lock) h = _handler;
            if (h is null) return;
            if (SendAccepted)
                await h(new Ack(cmd.ChangeId, AckLevel.Accepted, null), ct).ConfigureAwait(false);
            if (SendApplied)
                await h(new Ack(cmd.ChangeId, AckLevel.Applied, null), ct).ConfigureAwait(false);
            if (SendConverged)
                await h(new Ack(cmd.ChangeId, AckLevel.Converged, new ConvergedProof("in-memory", DateTimeOffset.UtcNow)), ct).ConfigureAwait(false);
            return;
        }

        // 其它消息（快照等）仅转发。
        Func<Message, CancellationToken, Task>? fwd;
        lock (_lock) fwd = _handler;
        if (fwd is not null)
            await fwd(message, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock) _handler = null;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly InMemoryTransport _owner;
        public Subscription(InMemoryTransport owner) => _owner = owner;
        public void Dispose()
        {
            lock (_owner._lock)
                _owner._handler = null;
        }
    }
}
