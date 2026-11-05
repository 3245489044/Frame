namespace Modules.Sync.Replica;

/// <summary>
/// 快照单调应用机：客户端唯一的状态推进入口。
/// <para>
/// 判据（R2）：仅接受 <c>tick &gt; lastAppliedTick</c> 的快照；
/// 同 tick 重复到达丢弃；旧 tick（乱序回退）丢弃。首帧没有水位，直接接受（不插值、不补帧）。
/// </para>
/// <para>
/// 依赖点（SY-00）：<c>Snapshot</c> 消息、<c>SyncStateProjection</c>、<c>StateHash</c>、
/// <c>ISyncReplica</c>、<c>SyncObserver</c> 尚未存在；本类不构造任何占位 DTO，
/// 只在 tick 水位这一层工作。契约到位后由 <c>SyncClientReplica</c> 在
/// <c>ApplySnapshot</c> 里先调用 <see cref="Apply"/>，接受后再把投影原样交给上层读取。
/// </para>
/// <para>
/// 传输依赖点：本类不持有 <c>ISyncTransport</c>、不起线程、不排队；收发与线程完全由宿主的
/// <c>ISyncTransport</c> 实现承担。
/// </para>
/// </summary>
public sealed class SnapshotGuard
{
    private readonly ReplicaTickLedger _ledger;
    private long _lastAppliedTick = -1L;
    private bool _hasApplied;
    private long _appliedCount;

    public SnapshotGuard(ReplicaTickLedger? ledger = null)
    {
        _ledger = ledger ?? new ReplicaTickLedger();
    }

    /// <summary>逐 tick 处置台账（只读取证面）。</summary>
    public ReplicaTickLedger Ledger => _ledger;

    /// <summary>当前已应用水位；只由被接受的快照推进。</summary>
    public AppliedState State => new(_hasApplied, _lastAppliedTick, _appliedCount);

    /// <summary>
    /// 纯判据：只回答「这条 tick 会不会被接受」，不改变任何水位与计数。
    /// </summary>
    public ReplicaTickOutcome Decide(long snapshotTick)
    {
        if (!_hasApplied)
            return ReplicaTickOutcome.Applied;

        if (snapshotTick <= _lastAppliedTick)
        {
            return snapshotTick == _lastAppliedTick
                ? ReplicaTickOutcome.SkippedDuplicateTick
                : ReplicaTickOutcome.SkippedStaleTick;
        }

        return ReplicaTickOutcome.Applied;
    }

    /// <summary>
    /// 应用入口：判据 + 推进水位 + 写台账，一次完成，避免「判了但没提交」的不一致。
    /// 被接受时返回 <see cref="ReplicaTickOutcome.Applied"/>，否则返回跳过理由。
    /// </summary>
    public ReplicaTickOutcome Apply(long snapshotTick)
    {
        ReplicaTickOutcome outcome = Decide(snapshotTick);
        long ordinal = 0L;

        if (outcome == ReplicaTickOutcome.Applied)
        {
            _lastAppliedTick = snapshotTick;
            _hasApplied = true;
            _appliedCount++;
            ordinal = _appliedCount;
        }

        _ledger.Record(snapshotTick, outcome, ordinal);
        return outcome;
    }
}
