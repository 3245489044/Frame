using Modules.Sync.Contracts;

namespace Modules.Sync.Replica;

/// <summary>
/// 客户端副本：只做三件事——握手、按 tick 单调应用快照、按窗口上送手动接管。
/// <para>
/// 只认 <see cref="ISyncTransport"/> 端口；不起线程、不自建队列、不做本地预测、不做回滚。
/// 本地可见状态只由被接受的下行 <see cref="Snapshot"/> 推进；哈希仅在同 tick 内与权威值比对。
/// </para>
/// </summary>
public sealed class SyncClientReplica : ISyncReplica
{
    private readonly ISyncTransport _transport;
    private readonly SyncObserver _observer;
    private readonly ulong? _expectedRulesetHash;
    private readonly SnapshotGuard _guard = new();
    private readonly TakeoverSubmitGate _gate;
    private SyncStateProjection? _lastApplied;
    private long _sessionId;
    private bool _connected;

    public SyncClientReplica(
        ISyncTransport transport,
        SyncObserver? observer = null,
        ulong? expectedRulesetHash = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        _transport = transport;
        _observer = observer ?? new NoopSyncObserver();
        _expectedRulesetHash = expectedRulesetHash;
        _gate = new TakeoverSubmitGate(SyncProtocol.InputWindowTicks);
    }

    /// <summary>当前已应用水位（tick、条数、最后哈希）。</summary>
    public AppliedState Applied => _guard.State;

    /// <summary>最后一条被接受快照的可见投影；未应用过时为 <c>null</c>。</summary>
    public SyncStateProjection? LastAppliedProjection => _lastApplied;

    /// <summary>逐 tick 处置台账（应用了什么、跳过了什么）。</summary>
    public ReplicaTickLedger Ledger => _guard.Ledger;

    /// <summary>接管上送窗口闸门（提供 <see cref="TakeoverSubmitGate.NextCommandSequence"/> 以分配单调 CommandId）。</summary>
    public TakeoverSubmitGate TakeoverGate => _gate;

    /// <summary>会话是否已建立（版本与可选规则集哈希均匹配）。</summary>
    public bool IsConnected => _connected;

    /// <summary>当前会话号；未建立时为 0。</summary>
    public long SessionId => _sessionId;

    /// <inheritdoc />
    public void Connect(Welcome welcome)
    {
        ArgumentNullException.ThrowIfNull(welcome);

        if (welcome.ProtocolVersion != SyncProtocol.ProtocolVersion)
        {
            _connected = false;
            _observer.OnHandshakeFailed(RejectReason.VersionMismatch);
            return;
        }

        if (_expectedRulesetHash is { } expected && welcome.RulesetHash != expected)
        {
            _connected = false;
            _observer.OnHandshakeFailed(RejectReason.RulesetMismatch);
            return;
        }

        _sessionId = welcome.SessionId;
        _connected = true;
        _observer.OnHandshakeCompleted(welcome.SessionId);
    }

    /// <inheritdoc />
    public bool ApplySnapshot(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!_connected)
        {
            // 未建会话不接受任何下行状态；不建会话也不推进水位。
            _observer.OnSnapshotDiscarded(snapshot.Tick);
            return false;
        }

        // 1) 单调判据只看 tick：旧/重复快照在此丢弃，不参与哈希比较。
        ReplicaTickOutcome verdict = _guard.Decide(snapshot.Tick);
        if (verdict != ReplicaTickOutcome.Applied)
        {
            _guard.RecordSkipped(snapshot.Tick, verdict);
            _observer.OnSnapshotDiscarded(snapshot.Tick);
            return false;
        }

        // 2) 同 tick 才比哈希（StateHash 含 Tick，跨 tick 比对无意义）。
        StateHash recomputed = StateHash.Compute(snapshot.Projection);
        if (!recomputed.Equals(snapshot.StateHash))
        {
            _guard.RecordSkipped(snapshot.Tick, ReplicaTickOutcome.SkippedHashMismatch);
            _observer.OnStateMismatch(snapshot.Tick);
            _observer.OnSnapshotDiscarded(snapshot.Tick);
            return false;
        }

        // 3) 校验通过才提交水位；本地状态全部来自这条投影，不做任何修补。
        _guard.CommitApplied(snapshot.Tick, recomputed);
        _lastApplied = snapshot.Projection;
        _observer.OnSnapshotApplied(snapshot.Tick);
        return true;
    }

    /// <inheritdoc />
    public void EnqueueCommand(TakeoverCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_connected)
        {
            _observer.OnInputRejected(RejectReason.Malformed);
            return;
        }

        TakeoverSubmitOutcome outcome = _gate.TrySubmit(Applied, command.TargetTick, command.CommandId);
        if (outcome != TakeoverSubmitOutcome.Accepted)
        {
            _observer.OnInputRejected(MapReason(outcome));
            return;
        }

        // 队列满不阻塞、不重试（MVP 用 sync.queueDepth 观测，不在协议层补偿）。
        _ = _transport.Send(SyncMessageKind.TakeoverCommand, command);
    }

    private static RejectReason MapReason(TakeoverSubmitOutcome outcome) => outcome switch
    {
        TakeoverSubmitOutcome.RejectedLate => RejectReason.Late,
        TakeoverSubmitOutcome.RejectedFuture => RejectReason.Future,
        TakeoverSubmitOutcome.RejectedDuplicate => RejectReason.Duplicate,
        _ => RejectReason.Malformed,
    };
}
