namespace Modules.Threading;

/// <summary>
/// 通用空闲看门狗（泛化自 AEF WatchdogService）：按**心跳**判定"空闲挂起 vs 推进"，
/// 超窗时触发回调。对任意心跳源（不限于 AsyncOperation）可复用；时钟可注入（FakeClock 可测）。
/// </summary>
/// <typeparam name="TItem">被监控项类型。</typeparam>
public sealed class IdleWatchdog<TItem> : IDisposable
    where TItem : class
{
    private readonly object _lock = new();
    private readonly Dictionary<TItem, TimeSpan> _tracked = new();
    private readonly Func<TItem, DateTimeOffset> _lastHeartbeat;
    private readonly Action<TItem> _onIdle;
    private readonly ITimingClock _clock;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private TimeSpan _pollInterval = TimingDefaults.AefPollInterval;

    public IdleWatchdog(Func<TItem, DateTimeOffset> lastHeartbeat, Action<TItem> onIdle, ITimingClock? clock = null)
    {
        _lastHeartbeat = lastHeartbeat ?? throw new ArgumentNullException(nameof(lastHeartbeat));
        _onIdle = onIdle ?? throw new ArgumentNullException(nameof(onIdle));
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>已注册项数。</summary>
    public int TrackedCount
    {
        get { lock (_lock) return _tracked.Count; }
    }

    /// <summary>启动轮询循环（幂等）。</summary>
    public void Start(TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "轮询周期必须 > 0");
        lock (_lock)
        {
            _pollInterval = pollInterval;
            _loop ??= Task.Run(RunLoopAsync);
        }
    }

    /// <summary>立即执行一次空闲扫描（测试可确定性触发；也供调用方手动即时检查）。</summary>
    public void CheckNow()
    {
        CheckIdle();
    }

    /// <summary>注册项并指定空闲窗口；&lt;=0 表示不启用该项看门狗。</summary>
    public void Track(TItem item, TimeSpan idleWindow)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (idleWindow <= TimeSpan.Zero)
            return;
        lock (_lock) _tracked[item] = idleWindow;
    }

    public void Untrack(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock) _tracked.Remove(item);
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
        KeyValuePair<TItem, TimeSpan>[] snapshot;
        lock (_lock) snapshot = _tracked.ToArray();

        var now = _clock.UtcNow;
        foreach (var (item, window) in snapshot)
        {
            if (now - _lastHeartbeat(item) > window)
                _onIdle(item);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
