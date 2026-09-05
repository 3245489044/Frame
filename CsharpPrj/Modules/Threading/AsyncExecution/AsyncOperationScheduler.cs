using System.Collections.Concurrent;

namespace Modules.Threading;

/// <summary>
/// 集中调度器：并发上限 + 空闲看门狗接入 + 防重入 + 隔离。
/// 超时 / 空闲挂起 / 主动取消 三态收敛为同一动作 → cancel → IProcessHandle.Kill()。
/// </summary>
public sealed class AsyncOperationScheduler : IDisposable
{
    /// <summary>诊断钩子（宿主注入日志器）；操作在超时/空闲/取消/异常终止时发出真实警告，缺省 no-op。</summary>
    public static Action<string, object?>? Trace { get; set; }

    private readonly SemaphoreSlim _gate;
    private readonly object _lock = new();
    private readonly int _maxConcurrency;
    private readonly TimeSpan _idleWindow;
    private readonly ConcurrentDictionary<Guid, AsyncOperation> _activeOps = new();
    private WatchdogService? _watchdog;
    private bool _ownsWatchdog;
    private int _runningCount;
    private bool _disposed;

    public AsyncOperationScheduler(int maxConcurrency, TimeSpan idleWindow, WatchdogService? watchdog = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "并发上限必须 > 0");
        _maxConcurrency = maxConcurrency;
        _gate = new SemaphoreSlim(maxConcurrency);
        _idleWindow = idleWindow > TimeSpan.Zero ? idleWindow : TimingDefaults.AefIdleWindow;
        _watchdog = watchdog;
    }

    public int MaxConcurrency => _maxConcurrency;

    /// <summary>已提交未终结的操作数（含排队中）。</summary>
    public int ActiveOpCount => _activeOps.Count;

    /// <summary>正在执行（进入并发闸门后）的操作数。</summary>
    public int RunningCount => Volatile.Read(ref _runningCount);

    public int QueuedCount => Math.Max(0, ActiveOpCount - RunningCount);

    public int IdleHungCount => _activeOps.Values.Count(o => o.Phase == AsyncOpPhase.IdleHung);

    /// <summary>绑定外部看门狗（装配时由 ThreadingBridge.InitializeAEF 调用）。</summary>
    public void AttachWatchdog(WatchdogService watchdog)
    {
        ArgumentNullException.ThrowIfNull(watchdog);
        lock (_lock)
        {
            if (_ownsWatchdog && _watchdog != null && !ReferenceEquals(_watchdog, watchdog))
            {
                _watchdog.Dispose();
                _ownsWatchdog = false;
            }

            _watchdog = watchdog;
        }
    }

    private WatchdogService EnsureWatchdog()
    {
        lock (_lock)
        {
            if (_watchdog != null) return _watchdog;

            var w = new WatchdogService();
            var pollMs = Math.Clamp((long)(_idleWindow.TotalMilliseconds / 4), 100, 2000);
            w.Start(TimeSpan.FromMilliseconds(pollMs), _idleWindow);
            _watchdog = w;
            _ownsWatchdog = true;
            return w;
        }
    }

    /// <summary>提交一个长任务（返回可 await 的结果契约）。</summary>
    public async Task<AsyncOperationResult> RunAsync(
        string name,
        Func<CancellationToken, AsyncOperation, Task<object?>> work,
        AsyncOperationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(work);
        options ??= new AsyncOperationOptions();

        if (options.ReentryKey != null && ReentryGuard.IsReentering(options.ReentryKey))
        {
            return new AsyncOperationResult
            {
                IsSuccess = false,
                Error = $"IsReentering: 相同 ReentryKey '{options.ReentryKey}' 正在执行，拒绝并发重复调用",
                ElapsedMs = 0,
                EndPhase = AsyncOpPhase.Failed
            };
        }

        var idle = options.IdleTimeout ?? _idleWindow;
        var watchdog = idle > TimeSpan.Zero ? EnsureWatchdog() : null;

        var op = new AsyncOperation<object?>(name, options.ExternalToken)
        {
            Process = options.Process,
            IdleWindow = idle > TimeSpan.Zero ? idle : null
        };

        _activeOps.TryAdd(op.Id, op);
        using var reentry = options.ReentryKey != null ? ReentryGuard.Enter(options.ReentryKey) : null;
        if (watchdog != null) watchdog.Track(op, idle);

        await _gate.WaitAsync();
        Interlocked.Increment(ref _runningCount);

        var started = DateTimeOffset.UtcNow;
        try
        {
            op.TransitionTo(AsyncOpPhase.Running);
            if (options.AbsoluteTimeout is { } at && at > TimeSpan.Zero)
                op.Cts.CancelAfter(at);

            Task<object?> workTask;
            try
            {
                workTask = work(op.Token, op);
            }
            catch (Exception ex)
            {
                op.Fail(ex);
                EmitTerminated(name, op.Phase, started, ex);
                return BuildResult(op, started, null, ex);
            }

            var absoluteDelay = options.AbsoluteTimeout is { } at2 && at2 > TimeSpan.Zero
                ? Task.Delay(at2)
                : null;

            var winner = absoluteDelay is null
                ? await Task.WhenAny(workTask, op.Completion)
                : await Task.WhenAny(workTask, op.Completion, absoluteDelay);

            object? value = null;
            Exception? error = null;

            if (ReferenceEquals(winner, workTask))
            {
                try
                {
                    value = await workTask;
                    op.Complete(value);
                }
                catch (OperationCanceledException oce)
                {
                    error = HandleCancelException(op, options, name, oce);
                }
                catch (Exception ex)
                {
                    op.Fail(ex);
                    error = ex;
                }
            }
            else if (absoluteDelay != null && ReferenceEquals(winner, absoluteDelay))
            {
                op.TransitionTo(AsyncOpPhase.TimedOut);
                op.Process?.Kill();
                error = new TimeoutException($"操作 '{name}' 超过绝对时限 {options.AbsoluteTimeout}");
                op.Fail(error);
            }
            else
            {
                // op.Completion 先行：看门狗(IdleHung) 或外部 Cancel 已终结
                op.Process?.Kill();
                error = op.Phase == AsyncOpPhase.IdleHung
                    ? new TimeoutException($"操作 '{name}' 空闲挂起")
                    : new OperationCanceledException($"操作 '{name}' 被取消");
            }

            // 真实警告：op 以非成功态终止时在此发出（超时/空闲/取消/异常），携带阶段与耗时——
            // 让"函数为什么被终止"在终止点可见，而非静默返回笼统结果。
            if (error != null)
                EmitTerminated(name, op.Phase, started, error);

            return BuildResult(op, started, value, error);
        }
        finally
        {
            Interlocked.Decrement(ref _runningCount);
            _gate.Release();
            if (watchdog != null) watchdog.Untrack(op);
            _activeOps.TryRemove(op.Id, out _);
            op.Dispose();
        }
    }

    private static Exception HandleCancelException(
        AsyncOperation op, AsyncOperationOptions options, string name, OperationCanceledException oce)
    {
        op.Process?.Kill();
        if (op.Phase == AsyncOpPhase.IdleHung)
        {
            return new TimeoutException($"操作 '{name}' 空闲挂起");
        }

        if (op.Phase == AsyncOpPhase.Cancelled)
        {
            return new OperationCanceledException($"操作 '{name}' 被取消");
        }

        // Fix·归因修正：外部取消（命令层等待超时/用户取消，经 ExternalToken 链接触发）应归 Cancelled，
        // 而非被后续 AbsoluteTimeout 分支误判为「框架绝对超时」（把外部取消的锅扣到框架执行层）。
        // 顺序：外部取消 > 绝对超时（仅 CancelAfter 到期且外部未请求时才算框架超时）。
        if (options.ExternalToken.IsCancellationRequested)
        {
            op.TransitionTo(AsyncOpPhase.Cancelled);
            op.Fail(oce);
            return new OperationCanceledException($"操作 '{name}' 被外部取消");
        }

        if (options.AbsoluteTimeout is { } at && at > TimeSpan.Zero)
        {
            op.TransitionTo(AsyncOpPhase.TimedOut);
            op.Fail(oce);
            return new TimeoutException($"操作 '{name}' 超过绝对时限 {at}");
        }

        op.Fail(oce);
        return oce;
    }

    /// <summary>在 op 非成功终止时发出真实警告（AEF_OpTerminated，Warning 级），携带阶段/耗时/错误。</summary>
    private static void EmitTerminated(string name, AsyncOpPhase phase, DateTimeOffset started, Exception? error)
    {
        var elapsedMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
        Trace?.Invoke("AEF_OpTerminated",
            new { name, phase = phase.ToString(), elapsedMs, error = error?.Message });
    }

    private static AsyncOperationResult BuildResult(
        AsyncOperation op, DateTimeOffset started, object? value, Exception? error)
    {
        var success = op.Phase == AsyncOpPhase.Completed && error == null;
        var endPhase = op.Phase;
        if (endPhase is not (AsyncOpPhase.Completed or AsyncOpPhase.Failed
            or AsyncOpPhase.Cancelled or AsyncOpPhase.TimedOut or AsyncOpPhase.IdleHung))
        {
            endPhase = success ? AsyncOpPhase.Completed : AsyncOpPhase.Failed;
        }

        return new AsyncOperationResult
        {
            IsSuccess = success,
            Output = success && value != null ? value.ToString() : null,
            Error = error?.Message,
            ElapsedMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            EndPhase = endPhase
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        lock (_lock)
        {
            if (_ownsWatchdog)
            {
                _watchdog?.Dispose();
                _ownsWatchdog = false;
            }
        }
    }

}
