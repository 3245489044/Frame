namespace Modules.Threading;

/// <summary>
/// 异步操作证据种类（**受控词表**，五层判活共用）——
/// <see cref="Byte"/> / <see cref="Control"/> / <see cref="Stage"/> / <see cref="Output"/> 属「推进」证据，
/// <see cref="Process"/> 只证明进程存活（L0），不推进判活。
/// </summary>
public enum AsyncEvidenceKind
{
    /// <summary>网络字节到达（流式首字节 / chunk）。</summary>
    Byte,

    /// <summary>控制帧（心跳 / 状态 / 阶段声明）。</summary>
    Control,

    /// <summary>阶段推进（跑题 / 工具调用 / 思考 / 回答；同名重复不算推进）。</summary>
    Stage,

    /// <summary>流式输出（增量文本 / delta）。</summary>
    Output,

    /// <summary>进程存活（L0，独立于推进证据）。</summary>
    Process,
}

/// <summary>
/// 操作证据快照（**判活宿主侧数据**）——回答「最后一次推进是什么时候、属于哪一层」。
/// </summary>
/// <param name="StartedAt">操作开始时刻。</param>
/// <param name="LastProgressAt">最后一次推进（Byte / Control / Stage（换名）/ Output）。</param>
/// <param name="LastByteAt">最后字节。</param>
/// <param name="LastControlAt">最后控制帧。</param>
/// <param name="LastStageAt">最后阶段推进。</param>
/// <param name="LastOutputAt">最后输出。</param>
/// <param name="LastProcessAt">最后进程存活证据（不算推进）。</param>
/// <param name="LastStageName">最后阶段名（同名重复不推进）。</param>
/// <param name="LastFrameKind">最后帧种类（诊断）。</param>
/// <param name="StageRepeatCount">同名阶段重复次数。</param>
public readonly record struct AsyncEvidence(
    DateTimeOffset StartedAt,
    DateTimeOffset LastProgressAt,
    DateTimeOffset? LastByteAt,
    DateTimeOffset? LastControlAt,
    DateTimeOffset? LastStageAt,
    DateTimeOffset? LastOutputAt,
    DateTimeOffset? LastProcessAt,
    string? LastStageName,
    string? LastFrameKind,
    int StageRepeatCount)
{
    /// <summary>距最后一次推进的时长（判活直接消费）。</summary>
    public TimeSpan ProgressAge => DateTimeOffset.UtcNow - LastProgressAt;
}

/// <summary>
/// 统一异步操作抽象基类（非泛型）：承载生命周期、取消贯穿、心跳与进程终止。
/// 调度器/看门狗仅依赖本基类，泛型层承载类型化结果。
/// </summary>
public abstract class AsyncOperation : IDisposable
{
    private readonly object _lock = new();
    private readonly object _evidenceLock = new();
    private AsyncOpPhase _phase = AsyncOpPhase.Pending;
    private bool _disposed;

    private DateTimeOffset _lastProgressAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastByteAt;
    private DateTimeOffset? _lastControlAt;
    private DateTimeOffset? _lastStageAt;
    private DateTimeOffset? _lastOutputAt;
    private DateTimeOffset? _lastProcessAt;
    private string? _lastStageName;
    private string? _lastFrameKind;
    private int _stageRepeat;

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

    /// <summary>
    /// 归属标记（调用键 / 执行端名 / 成员名）——由发起方或调度器写入，供看门狗与结算报告把「在飞」映射回「谁」。
    /// 写者：调用方；清理：操作终结（终态后只读，不再改写）。
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>唯一完成信号（泛型派生提供类型化 Task；基类不持有第二份完成源）。</summary>
    public abstract Task Completion { get; }

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

    /// <summary>最后一次有效推进时刻（Byte / Control / Stage（换名）/ Output；进程存活不算）。</summary>
    public DateTimeOffset LastProgressAt
    {
        get { lock (_evidenceLock) return _lastProgressAt; }
    }

    /// <summary>
    /// 登记一次推进证据（**五层判活的宿主侧数据入口**）。
    /// <para>
    /// 写者：传输线（Byte / Control）、执行器线（Stage / Output）、看门狗（Process）。
    /// Stage 同名重复不推进（只累计 <see cref="Evidence"/> 的重复数）——「原地打转」必须被判活看见，而不是被心跳掩盖。
    /// </para>
    /// </summary>
    public void ReportEvidence(AsyncEvidenceKind kind, string? detail = null)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_evidenceLock)
        {
            switch (kind)
            {
                case AsyncEvidenceKind.Byte:
                    _lastByteAt = now;
                    Advance(now, detail);
                    break;
                case AsyncEvidenceKind.Control:
                    _lastControlAt = now;
                    Advance(now, detail);
                    break;
                case AsyncEvidenceKind.Stage:
                    _lastStageAt = now;
                    if (string.Equals(_lastStageName, detail, StringComparison.Ordinal))
                    {
                        _stageRepeat++;
                    }
                    else
                    {
                        _lastStageName = detail;
                        _stageRepeat = 0;
                        Advance(now, detail);
                    }

                    break;
                case AsyncEvidenceKind.Output:
                    _lastOutputAt = now;
                    Advance(now, detail);
                    break;
                case AsyncEvidenceKind.Process:
                    _lastProcessAt = now;   // L0：进程存活不是推进
                    break;
                default:
                    Advance(now, detail);
                    break;
            }
        }

        LastHeartbeatAt = now;
    }

    /// <summary>证据快照（只读；判活策略据此决策，本类不做判活）。</summary>
    public AsyncEvidence Evidence
    {
        get
        {
            lock (_evidenceLock)
            {
                return new AsyncEvidence(
                    StartedAt,
                    _lastProgressAt,
                    _lastByteAt,
                    _lastControlAt,
                    _lastStageAt,
                    _lastOutputAt,
                    _lastProcessAt,
                    _lastStageName,
                    _lastFrameKind,
                    _stageRepeat);
            }
        }
    }

    private void Advance(DateTimeOffset now, string? detail)
    {
        _lastProgressAt = now;
        if (!string.IsNullOrWhiteSpace(detail)) _lastFrameKind = detail;
    }

    public abstract void Complete(object? result);

    public abstract void Fail(Exception ex);

    public virtual void Cancel()
    {
        FinishTerminal(AsyncOpPhase.Cancelled, static () => { });
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
    public override Task<T> Completion => _typed.Task;

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
        });
    }

    public override void Complete(object? result) => Complete((T)result!);

    public override void Fail(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        FinishTerminal(AsyncOpPhase.Failed, () =>
        {
            _typed.TrySetException(ex);
        });
        // E1：故障副本必须有观察者——await 侧仍会收到异常，但不再触发进程级 UnobservedTaskException。
        ObserveFailure(_typed.Task);
        Cts.Cancel();
    }

    public override void Cancel()
    {
        FinishTerminal(AsyncOpPhase.Cancelled, () =>
        {
            _typed.TrySetCanceled();
        });
        Process?.Kill();
        Cts.Cancel();
    }

    /// <summary>标记故障 Task 已观察（不改变 await 语义）。</summary>
    private static void ObserveFailure(Task task)
    {
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
