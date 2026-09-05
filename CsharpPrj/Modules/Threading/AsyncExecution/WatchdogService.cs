namespace Modules.Threading;

/// <summary>
/// 空闲看门狗：以 pollInterval 轮询所有已注册操作，按 Heartbeat 判定"空闲挂起"并终止。
/// 空闲语义 = 心跳，而非固定期限。
/// </summary>
public sealed class WatchdogService : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<AsyncOperation, TimeSpan> _tracked = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ITimingClock _clock;
    private Task? _loop;
    private TimeSpan _pollInterval = TimingDefaults.AefPollInterval;
    private TimeSpan _idleWindow = TimingDefaults.AefIdleWindow;

    /// <param name="clock">可选时钟（默认 SystemClock；注入 FakeClock 可测空闲判定）。</param>
    public WatchdogService(ITimingClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    public int TrackedCount
    {
        get { lock (_lock) return _tracked.Count; }
    }

    /// <summary>启动轮询循环。</summary>
    public void Start(TimeSpan pollInterval, TimeSpan idleWindow)
    {
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "轮询周期必须 > 0");
        if (idleWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleWindow), "空闲窗口必须 > 0");

        lock (_lock)
        {
            _pollInterval = pollInterval;
            _idleWindow = idleWindow;
            _loop ??= Task.Run(RunLoopAsync);
        }
    }

    /// <summary>注册操作（使用全局默认空闲窗口）。</summary>
    public void Track(AsyncOperation op) => Track(op, _idleWindow);

    /// <summary>注册操作并指定其空闲窗口；&lt;=0 表示不启用该操作的空闲看门狗。</summary>
    public void Track(AsyncOperation op, TimeSpan idleWindow)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (idleWindow <= TimeSpan.Zero) return;
        lock (_lock) _tracked[op] = idleWindow;
    }

    public void Untrack(AsyncOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        lock (_lock) _tracked.Remove(op);
    }

    private async Task RunLoopAsync()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            CheckIdle();
        }
    }

    private void CheckIdle()
    {
        KeyValuePair<AsyncOperation, TimeSpan>[] snapshot;
        lock (_lock) snapshot = _tracked.ToArray();

        var now = _clock.UtcNow;
        foreach (var (op, window) in snapshot)
        {
            if (op.IsTerminal) continue;
            var last = op.LastHeartbeatAt;
            if (now - last > window)
            {
                op.TransitionTo(AsyncOpPhase.IdleHung);
                op.Process?.Kill();
                op.Fail(new TimeoutException($"操作 '{op.Name}' 空闲挂起（{window} 无心跳）"));
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
