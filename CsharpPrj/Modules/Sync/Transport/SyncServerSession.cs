using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>会话生命周期：<c>Created → Handshaking → Active → Closed</c>；拒绝路径直达 <c>Closed</c>，不建会话。</summary>
public enum SyncSessionState
{
    /// <summary>已创建，尚未发起握手。</summary>
    Created = 0,

    /// <summary>握手中：只等 Hello 裁决，不接受任何输入。</summary>
    Handshaking = 1,

    /// <summary>握手通过，会话可用：接受输入、可推进 tick。</summary>
    Active = 2,

    /// <summary>已关闭：握手被拒、或主动关闭；任何输入都被拒并回调观察者。</summary>
    Closed = 3,
}

/// <summary>
/// 服务端会话排序机：握手窗口 + 输入窗口 + 幂等键 + 确定性排序 + tick 推进的唯一编排点。
/// <para>单线程模型：只由权威循环线程独占调用，内部不加锁；与 <see cref="InMemorySyncTransport"/> 协作时，
/// 锁序固定为“会话逻辑在前、传输队列在后”，且绝不在持锁期间回调外部。</para>
/// <para>输入拒绝一律落到契约 <see cref="RejectReason"/> 并经 <see cref="SyncObserver.OnInputRejected"/> 上报：
/// 窗口外 → <c>Late/Future</c>；同 <c>(tick, CommandId)</c> 重复或同槽位已有首条 → <c>Duplicate</c>；
/// 会话未激活等协议外调用 → <c>Malformed</c>。</para>
/// </summary>
public sealed class SyncServerSession
{
    private readonly long _sessionId;
    private readonly SyncHandshakeGate _handshake;
    private readonly SyncObserver _observer;
    private readonly SyncInputWindow _window;
    private readonly SyncIdempotencyWindow<long> _idempotency;
    private readonly SyncInputOrdering _ordering;
    private SyncSessionState _state = SyncSessionState.Created;
    private RejectReason? _handshakeRejection;

    /// <param name="sessionId">会话号（<see cref="Welcome.SessionId"/> 的值，由权威端分配）。</param>
    /// <param name="handshake">握手校验器（版本 + 规则集哈希）。</param>
    /// <param name="observer">观测端口；缺省空观察者。</param>
    /// <param name="initialTick">初始决策 tick（从 0 起）。</param>
    public SyncServerSession(
        long sessionId,
        SyncHandshakeGate handshake,
        SyncObserver? observer = null,
        long initialTick = 0)
    {
        ArgumentNullException.ThrowIfNull(handshake);

        _sessionId = sessionId;
        _handshake = handshake;
        _observer = observer ?? new NoopSyncObserver();
        _window = new SyncInputWindow(initialTick);
        _idempotency = new SyncIdempotencyWindow<long>(SyncProtocol.InputWindowTicks, initialTick);
        _ordering = new SyncInputOrdering();
    }

    /// <summary>当前生命周期状态。</summary>
    public SyncSessionState State => _state;

    /// <summary>会话号。</summary>
    public long SessionId => _sessionId;

    /// <summary>当前决策 tick。</summary>
    public long CurrentTick => _window.CurrentTick;

    /// <summary>最近一次握手拒绝原因；<c>null</c> 表示未拒绝（尚未握手或已通过）。</summary>
    public RejectReason? LastHandshakeRejection => _handshakeRejection;

    /// <summary>握手是否已被拒绝（拒绝即关闭，且不建会话）。</summary>
    public bool IsRejected => _state == SyncSessionState.Closed && _handshakeRejection is not null;

    /// <summary>从 <c>Created</c> 进入 <c>Handshaking</c>；其它状态返回 false 且不改状态。</summary>
    public bool TryBeginHandshake()
    {
        if (_state != SyncSessionState.Created)
        {
            return false;
        }

        _state = SyncSessionState.Handshaking;
        return true;
    }

    /// <summary>
    /// 裁决一次 <see cref="Hello"/>。仅 <c>Handshaking</c> 可调用；其它状态下视为畸形握手并拒绝。
    /// 版本不匹配 → <see cref="RejectReason.VersionMismatch"/>，载荷畸形 → <see cref="RejectReason.Malformed"/>；
    /// 拒绝时状态置 <c>Closed</c>、不建会话、回调 <see cref="SyncObserver.OnHandshakeFailed"/>。
    /// </summary>
    public RejectReason? SubmitHello(Hello hello)
    {
        if (_state != SyncSessionState.Handshaking)
        {
            return RejectHandshake(RejectReason.Malformed);
        }

        var reason = _handshake.EvaluateHello(hello);
        if (reason is not null)
        {
            return RejectHandshake(reason.Value);
        }

        _state = SyncSessionState.Active;
        _observer.OnHandshakeCompleted(_sessionId);
        return null;
    }

    /// <summary>主动关闭；重复关闭无副作用。</summary>
    public void Close() => _state = SyncSessionState.Closed;

    /// <summary>推进当前 tick（单调）；同时回收滑出窗口的幂等桶与排序桶。</summary>
    public void AdvanceTo(long tick)
    {
        if (_state != SyncSessionState.Active)
        {
            throw new InvalidOperationException("会话未激活，不能推进决策 tick。");
        }

        _window.AdvanceTo(tick);
        var current = _window.CurrentTick;
        _idempotency.AdvanceTo(current);
        _ordering.AdvanceTo(current);
    }

    /// <summary>
    /// 输入准入：窗口三段语义 → 同 tick 已消费 → 幂等 → 确定性排序（首条优先）。
    /// 返回 <c>null</c> 表示接受；否则为拒绝原因（已回调 <see cref="SyncObserver.OnInputRejected"/>）。
    /// </summary>
    public RejectReason? TryAcceptInput(TakeoverCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_state != SyncSessionState.Active)
        {
            return Reject(RejectReason.Malformed);
        }

        var reason = _window.Classify(command.TargetTick);
        if (reason is not null)
        {
            return Reject(reason.Value);
        }

        if (_ordering.IsTickConsumed(command.TargetTick))
        {
            return Reject(RejectReason.Late);
        }

        if (_idempotency.IsRegistered(command.TargetTick, command.CommandId))
        {
            return Reject(RejectReason.Duplicate);
        }

        if (!_ordering.TryReserve(command, out var evicted))
        {
            return Reject(_ordering.IsTickConsumed(command.TargetTick)
                ? RejectReason.Late
                : RejectReason.Duplicate);
        }

        if (evicted is not null)
        {
            // 被淘汰者从未真正应用，撤销其幂等登记，避免其重试被误判为重复。
            _idempotency.Revoke(evicted.TargetTick, evicted.CommandId);
        }

        _idempotency.TryRegister(command.TargetTick, command.CommandId);
        return null;
    }

    /// <summary>
    /// 消费某 tick：按 <c>TargetTick → Kind → CommandId</c> 全序输出该 tick 胜出的接管命令，
    /// 并把该 tick 标记为已消费（之后同 tick 输入拒收）。输出顺序即 <see cref="ISyncAuthority.ApplyInput"/> 的应用顺序。
    /// </summary>
    public int DrainTickInputs(long tick, TakeoverCommand[] sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_state != SyncSessionState.Active)
        {
            return 0;
        }

        return _ordering.DrainTick(tick, sink);
    }

    private RejectReason? Reject(RejectReason reason)
    {
        _observer.OnInputRejected(reason);
        return reason;
    }

    private RejectReason? RejectHandshake(RejectReason reason)
    {
        _handshakeRejection = reason;
        _state = SyncSessionState.Closed;
        _observer.OnHandshakeFailed(reason);
        return reason;
    }
}
