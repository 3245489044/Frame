namespace Modules.Threading;

/// <summary>
/// 统一异步操作抽象基类（非泛型）：承载生命周期、取消贯穿、心跳与进程终止。
/// 调度器/看门狗仅依赖本基类，泛型层承载类型化结果。
/// </summary>
public abstract class AsyncOperation : IDisposable
{
    private readonly object _lock = new();
    protected readonly TaskCompletionSource _lifecycle =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AsyncOpPhase _phase = AsyncOpPhase.Pending;
    private bool _disposed;

    /// <summary>内部取消源（超时/空闲/外部取消统一经此触发）。</summary>
    private readonly CancellationTokenSource _cts;

    protected AsyncOperation(string name)
        : this(name, CancellationToken.None)
    {
    }

    /// <param name="externalToken">可选外部取消令牌：与之链接，外部取消会同步取消本操作（如命令层等待超时）。</param>
    protected AsyncOperation(string name, CancellationToken externalToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        _cts = externalToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
            : new CancellationTokenSource();
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; }

    /// <summary>取消贯穿源：超时 / 空闲挂起 / 主动取消 三态收敛到此。</summary>
    public CancellationTokenSource Cts => _cts;

    public CancellationToken Token => Cts.Token;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>看门狗输入：最后心跳时间。</summary>
    public DateTimeOffset LastHeartbeatAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>可杀对象（如 node / ClineCore 进程）。</summary>
    public IProcessHandle? Process { get; set; }

    /// <summary>非泛型完成信号（终结即落定）。</summary>
    public Task Completion => _lifecycle.Task;

    public AsyncOpPhase Phase
    {
        get { lock (_lock) return _phase; }
    }

    public bool IsTerminal => IsTerminalPhase(Phase);

    /// <summary>本操作的空闲兜底窗口（由调度器按 options 解析注入；看门狗读取）。</summary>
    internal TimeSpan? IdleWindow { get; set; }

    public event EventHandler<AsyncOpPhaseChangedEventArgs>? PhaseChanged;

    /// <summary>心跳：任何"仍在推进"的证据（流式输出 / 步骤推进）都应调用。</summary>
    public void ReportProgress(object? value) => TouchHeartbeat();

    public void TouchHeartbeat() => LastHeartbeatAt = DateTimeOffset.UtcNow;

    public abstract void Complete(object? result);

    public abstract void Fail(Exception ex);

    public virtual void Cancel()
    {
        FinishTerminal(AsyncOpPhase.Cancelled, () => _lifecycle.TrySetResult());
        Process?.Kill();
        Cts.Cancel();
    }

    /// <summary>仅切换状态（不落定完成信号）；供看门狗标记 IdleHung 后由 Fail 落定。</summary>
    internal void TransitionTo(AsyncOpPhase next)
    {
        lock (_lock)
        {
            if (IsTerminalPhase(_phase) || _phase == next) return;
            var old = _phase;
            _phase = next;
            PhaseChanged?.Invoke(this, new AsyncOpPhaseChangedEventArgs(old, next));
        }
    }

    /// <summary>终结统一入口：已终结则仅执行 resolve（不覆盖状态），否则转移并落定。</summary>
    protected void FinishTerminal(AsyncOpPhase phase, Action resolve)
    {
        lock (_lock)
        {
            if (IsTerminalPhase(_phase))
            {
                resolve();
                return;
            }

            var old = _phase;
            _phase = phase;
            PhaseChanged?.Invoke(this, new AsyncOpPhaseChangedEventArgs(old, phase));
            resolve();
        }
    }

    protected static bool IsTerminalPhase(AsyncOpPhase phase)
        => phase is AsyncOpPhase.Completed
            or AsyncOpPhase.Failed
            or AsyncOpPhase.Cancelled
            or AsyncOpPhase.TimedOut
            or AsyncOpPhase.IdleHung;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!IsTerminal) Cancel();
        Cts.Dispose();
    }
}

/// <summary>统一异步操作（泛型）：类型化完成信号 <see cref="Completion"/> 取代散落 TCS。</summary>
public sealed class AsyncOperation<T> : AsyncOperation
{
    private readonly TaskCompletionSource<T> _typed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AsyncOperation(string name) : base(name)
    {
    }

    /// <param name="externalToken">可选外部取消令牌：与之链接，外部取消会同步取消本操作。</param>
    public AsyncOperation(string name, CancellationToken externalToken) : base(name, externalToken)
    {
    }

    /// <summary>类型化完成信号（唯一完成信号）。</summary>
    public new Task<T> Completion => _typed.Task;

    public T? Result
    {
        get
        {
            try
            {
                return _typed.Task.Status == TaskStatus.RanToCompletion
                    ? _typed.Task.Result
                    : default;
            }
            catch
            {
                return default;
            }
        }
    }

    public void Complete(T result)
    {
        FinishTerminal(AsyncOpPhase.Completed, () =>
        {
            _typed.TrySetResult(result);
            _lifecycle.TrySetResult();
        });
    }

    public override void Complete(object? result) => Complete((T)result!);

    public override void Fail(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        FinishTerminal(AsyncOpPhase.Failed, () =>
        {
            _typed.TrySetException(ex);
            _lifecycle.TrySetResult();
        });
        Cts.Cancel();
    }

    public override void Cancel()
    {
        FinishTerminal(AsyncOpPhase.Cancelled, () =>
        {
            _typed.TrySetCanceled();
            _lifecycle.TrySetResult();
        });
        Process?.Kill();
        Cts.Cancel();
    }
}
