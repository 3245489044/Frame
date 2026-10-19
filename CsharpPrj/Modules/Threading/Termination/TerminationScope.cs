using System;
using System.Collections.Generic;
using System.Threading;

namespace Modules.Threading;

/// <summary>
/// 终止作用域 —— 唯一「有界 + 强制退出」载体（机制层，零领域依赖）。
/// <para>三件事：① 传播预算（子作用域 = min(自身, 父剩余)，父取消级联子）；② 心跳判空闲
/// （只允许在真实推进时 <see cref="Beat"/>，禁空转续命）；③ 强制退出
/// （<see cref="Terminate"/> 只标记终态 + 取消 + 跑非阻塞回调，**不等待被取消方返回**）。</para>
/// <para>与既有能力的关系（SingleImplementation）：预算换算复用 <see cref="ITimingClock"/>；
/// 取消树复用 <see cref="CancellationTokenSource"/> linked-token；轮询判空闲复用
/// <see cref="IdleWatchdog{TItem}"/>（本类只提供心跳访问器）；有界执行复用
/// <see cref="BoundedExecution"/>/<see cref="BoundedRun"/>/<see cref="TimeBudget"/>。</para>
/// <para>ambient 传播经 <see cref="AmbientSlot{T}"/>：存储的是**作用域句柄**（不是可写状态副本），
/// 与 <see cref="ExecutionScope"/> 同先例；后台 worker 边界须显式传入或用 <see cref="FlowBoundary"/> 切断。</para>
/// </summary>
public sealed class TerminationScope : IDisposable
{
    private static readonly AmbientSlot<TerminationScope> Ambient =
        new("TerminationScope.Current");

    private readonly object _sync = new();
    private readonly CancellationTokenSource _cts;
    private readonly ITimingClock _clock;
    private readonly TerminationBudget _budget;
    private readonly TimeSpan? _idleWindow;
    private readonly DateTimeOffset _createdAt;
    private readonly DateTimeOffset _deadline;
    private readonly List<Action<TerminationRequest>> _abortHandlers = new();

    private IDisposable? _ambientHandle;
    private DateTimeOffset _lastBeat;
    private long _beatCount;
    private TerminationRequest? _request;
    private bool _completed;
    private bool _disposed;

    private TerminationScope(string owner, TerminationBudget budget, TerminationScope? parent, ITimingClock clock)
    {
        Owner = owner;
        _budget = budget ?? TerminationBudget.Unbounded;
        _clock = clock;
        _idleWindow = _budget.Idle;
        _createdAt = clock.UtcNow;
        _lastBeat = _createdAt;
        ScopeId = Guid.NewGuid().ToString("N");

        var ownDeadline = _budget.Total is { } total && total > TimeSpan.Zero
            ? _createdAt + total
            : DateTimeOffset.MaxValue;
        var inheritedDeadline = parent?._deadline ?? DateTimeOffset.MaxValue;
        _deadline = ownDeadline <= inheritedDeadline ? ownDeadline : inheritedDeadline;

        _cts = parent is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(parent.Token);
    }

    /// <summary>当前逻辑执行链上的作用域（宿主入口 Begin 后自动可见）。</summary>
    public static TerminationScope? Current => Ambient.Current;

    /// <summary>作用域标识（审计关联键）。</summary>
    public string ScopeId { get; }

    /// <summary>作用域属主（宿主义务形如 script / send / action:ExecuteRound）。</summary>
    public string Owner { get; }

    /// <summary>取消令牌（协作取消/探索终止载体；子作用域自动级联）。</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// 剩余预算；无 deadline 时返回 <see cref="Timeout.InfiniteTimeSpan"/>（可直接用于
    /// <c>Task.WaitAsync(TimeSpan)</c>）。判定有界性请用 <see cref="HasDeadline"/>。
    /// </summary>
    public TimeSpan Remaining
    {
        get
        {
            if (_deadline == DateTimeOffset.MaxValue) return Timeout.InfiniteTimeSpan;
            var remaining = _deadline - _clock.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>是否存在总预算 deadline（false = Remaining 为 Infinite）。</summary>
    public bool HasDeadline => _deadline != DateTimeOffset.MaxValue;

    /// <summary>空闲窗口（null = 不启用空闲判定）。</summary>
    public TimeSpan? IdleWindow => _idleWindow;

    /// <summary>距上一次真实推进心跳的时长。</summary>
    public TimeSpan IdleFor
    {
        get
        {
            var idle = _clock.UtcNow - _lastBeat;
            return idle > TimeSpan.Zero ? idle : TimeSpan.Zero;
        }
    }

    /// <summary>空闲是否已超窗（有窗口才可能为 true）。</summary>
    public bool IsIdleExpired => _idleWindow is { } window && IdleFor > window;

    /// <summary>已运行时长。</summary>
    public TimeSpan Elapsed => _clock.UtcNow - _createdAt;

    /// <summary>终止理由（null = 未终止）。</summary>
    public TerminationReason? Reason => _request?.Reason;

    /// <summary>终止证据（结构化，供审计/日志；null = 未终止）。</summary>
    public TerminationRequest? Request => _request;

    /// <summary>是否已终止（含外部取消令牌触发，但未显式 Terminate 时为准）。</summary>
    public bool IsTerminated => _request is not null || _cts.IsCancellationRequested;

    /// <summary>是否自然完成（由宿主在正常收敛后调用 <see cref="Complete"/> 标记）。</summary>
    public bool IsCompleted => _completed;

    /// <summary>真实推进心跳累计次数（观测用；只统计真实推进）。</summary>
    public long BeatCount => Interlocked.Read(ref _beatCount);

    /// <summary>预算声明（只读）。</summary>
    public TerminationBudget Budget => _budget;

    /// <summary>最近一次心跳的推进点标签（诊断用）。</summary>
    public string? LastProgressPoint { get; private set; }

    /// <summary>
    /// 创建（或进入）一个终止作用域：预算与父作用域取 min，取消令牌链接父级。
    /// </summary>
    /// <param name="owner">属主标识（用于审计关联）。</param>
    /// <param name="budget">本作用域预算（null 维度=不限）。</param>
    /// <param name="parent">父作用域；null = 取 <see cref="Current"/>（无则以自身为根）。</param>
    /// <param name="clock">可注入时钟（测试用 <see cref="FakeClock"/>）。</param>
    public static TerminationScope Begin(
        string owner,
        TerminationBudget budget,
        TerminationScope? parent = null,
        ITimingClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("作用域属主不能为空。", nameof(owner));

        var effectiveParent = parent ?? Ambient.Current;
        var scope = new TerminationScope(owner, budget, effectiveParent, clock ?? SystemClock.Instance);
        scope._ambientHandle = Ambient.Enter(scope);
        return scope;
    }

    /// <summary>
    /// 派生层级子作用域：独立 linked-CTS（可单独终止、级联父取消），预算取 min。
    /// 不进 ambient（调用方显式传 <see cref="Token"/>，避免隐式上下文串味）。
    /// </summary>
    public TerminationScope CreateChild(string owner, TerminationBudget budget)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("子作用域属主不能为空。", nameof(owner));
        return new TerminationScope(owner, budget, this, _clock);
    }

    /// <summary>
    /// 真实推进心跳：只在「产生可验证推进」时调用（轮次推进/命令完成/信号终态）。
    /// 空转轮、仅报告轮**不得**调用，否则构成假活。
    /// </summary>
    public void Beat(string progressPoint)
    {
        if (_request is not null) return;
        _lastBeat = _clock.UtcNow;
        Interlocked.Increment(ref _beatCount);
        LastProgressPoint = progressPoint;
    }

    /// <summary>
    /// 强制退出：标记终态 → 取消令牌 → 依次执行非阻塞中止回调（每个回调异常被隔离）。
    /// **立即返回**，不等待被取消方返回（对齐「到边界必须强制终止，不得依赖用户关窗」）。
    /// </summary>
    /// <returns>true = 本次首次触发；false = 已终止（幂等，保留首个理由）。</returns>
    public bool Terminate(TerminationReason reason, string? evidence = null)
    {
        if (reason is TerminationReason.None or TerminationReason.Completed)
            throw new ArgumentOutOfRangeException(nameof(reason), "Terminate 只接受失败/终止类理由。");

        Action<TerminationRequest>[] handlers;
        TerminationRequest request;
        lock (_sync)
        {
            if (_request is not null) return false;
            request = new TerminationRequest(
                ScopeId, Owner, reason, evidence, _clock.UtcNow, Elapsed, Forced: true);
            _request = request;
            handlers = _abortHandlers.ToArray();
            _abortHandlers.Clear();
        }

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* 已释放：终止理由已落，取消树已失效 */ }

        foreach (var handler in handlers)
        {
            try { handler(request); }
            catch { /* 中止回调不得反向阻断强制退出（每个回调自行记录） */ }
        }

        return true;
    }

    /// <summary>
    /// 注册强制中止回调（契约：非阻塞，只做门/租约释放/终态标记，禁 await 长任务）。
    /// 若作用域已终止，回调立即执行（可见化，不静默丢弃）。
    /// </summary>
    public IDisposable RegisterAbort(Action<TerminationRequest> onAbort)
    {
        ArgumentNullException.ThrowIfNull(onAbort);

        TerminationRequest? already;
        lock (_sync)
        {
            if (_request is null)
            {
                _abortHandlers.Add(onAbort);
                return new AbortRegistration(this, onAbort);
            }
            already = _request;
        }

        try { onAbort(already); }
        catch { /* 同上：回调异常不阻断调用方 */ }
        return EmptyDisposable.Instance;
    }

    /// <summary>标记自然完成（不取消；<see cref="Dispose"/> 时才释放取消树）。</summary>
    public void Complete()
    {
        lock (_sync) _completed = true;
    }

    private void RemoveAbort(Action<TerminationRequest> onAbort)
    {
        lock (_sync) _abortHandlers.Remove(onAbort);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _ambientHandle?.Dispose();
        _ambientHandle = null;

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
        _cts.Dispose();
    }

    private sealed class AbortRegistration : IDisposable
    {
        private readonly TerminationScope _scope;
        private readonly Action<TerminationRequest> _handler;
        private bool _disposed;

        public AbortRegistration(TerminationScope scope, Action<TerminationRequest> handler)
        {
            _scope = scope;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scope.RemoveAbort(_handler);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}
