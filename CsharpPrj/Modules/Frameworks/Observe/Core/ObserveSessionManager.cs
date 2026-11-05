using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Observe;

/// <summary>
/// 收敛层核心协调器（<see cref="IObserveCoordinator"/> 实现）：维护每变更的 <see cref="ObserveSession"/> 状态机，
/// 按 <see cref="WriteOptions.Barrier"/> 决定返回时机；经传输的 ack 链升级会话，到 Converged 或超时降级后终结。
/// 只依赖 <see cref="ITransport"/> 与协议抽象，不感知具体通道。
/// </summary>
public sealed class ObserveSessionManager : IObserveCoordinator, IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly ITransport _transport;
    private readonly IDisposable _subscription;
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private long _versionCounter;
    private bool _disposed;

    public ObserveSessionManager(ITransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _subscription = transport.Subscribe(OnMessageAsync);
    }

    public async Task<ObserveOutcome> WriteAsync<T>(
        string key, T value, WriteOptions? options = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new WriteOptions();

        var id = Guid.NewGuid().ToString("N");
        var version = Interlocked.Increment(ref _versionCounter);
        var timeout = options.WindowTimeout ?? DefaultTimeout;
        var session = new ObserveSession(id, key, version, options.Barrier, timeout);
        var tcs = new TaskCompletionSource<ObserveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions[id] = new SessionEntry(session, tcs);

        // 取消即落 Failed（显式结果，不静默）；send 期间取消也能覆盖。
        using var cancelReg = ct.Register(() =>
            tcs.TrySetResult(new ObserveOutcome(id, key, version, session.HighestAck, TimeSpan.Zero, ChangeResult.Failed)));

        try
        {
            await _transport.SendAsync(new ChangeCommand<T>(id, key, version, value), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _sessions.TryRemove(id, out _);
            throw;
        }
        catch
        {
            _sessions.TryRemove(id, out _);
            return new ObserveOutcome(id, key, version, session.HighestAck, TimeSpan.Zero, ChangeResult.Failed);
        }

        if (options.Barrier == BarrierMode.FireAndForget)
        {
            // FireAndForget：立即返回，不等待收敛（调用方放弃 I3）；HighestAck 反映投递阶段。收敛由回调/事件通知。
            _sessions.TryRemove(id, out _);
            return new ObserveOutcome(id, key, version, session.HighestAck, TimeSpan.Zero, ChangeResult.Committed);
        }

        if (options.Barrier == BarrierMode.Strict)
        {
            try
            {
                return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ObserveOutcome(id, key, version, session.HighestAck, TimeSpan.Zero, ChangeResult.Failed);
            }
            finally
            {
                _sessions.TryRemove(id, out _);
            }
        }

        // Barrier == Timeout：收敛窗口有界，界内未 Converged → Degraded（I4 显式降级，绝不静默成功）。
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
        if (done == tcs.Task)
        {
            var outcome = await tcs.Task.ConfigureAwait(false);
            _sessions.TryRemove(id, out _);
            return outcome;
        }
        _sessions.TryRemove(id, out _);
        return new ObserveOutcome(id, key, version, session.HighestAck,
            (session.ConvergedAt - session.StartedAt) ?? TimeSpan.Zero, ChangeResult.Degraded);
    }

    private async Task OnMessageAsync(Message message, CancellationToken ct)
    {
        if (message is not Ack ack)
            return;
        if (!_sessions.TryGetValue(ack.ChangeId, out var entry))
            return;

        var session = entry.Session;
        session.Upgrade(ack.Level, DateTimeOffset.UtcNow);
        if (ack.Level == AckLevel.Converged)
        {
            entry.Tcs.TrySetResult(new ObserveOutcome(
                session.ChangeId, session.Key, session.Version, session.HighestAck,
                (session.ConvergedAt - session.StartedAt) ?? TimeSpan.Zero, ChangeResult.Committed));
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription.Dispose();
        _transport.Dispose();
        foreach (var (_, entry) in _sessions)
            entry.Tcs.TrySetResult(new ObserveOutcome(
                entry.Session.ChangeId, entry.Session.Key, entry.Session.Version,
                entry.Session.HighestAck, TimeSpan.Zero, ChangeResult.Failed));
        _sessions.Clear();
    }

    private sealed class SessionEntry
    {
        public SessionEntry(ObserveSession session, TaskCompletionSource<ObserveOutcome> tcs)
        {
            Session = session;
            Tcs = tcs;
        }

        public ObserveSession Session { get; }
        public TaskCompletionSource<ObserveOutcome> Tcs { get; }
    }
}
