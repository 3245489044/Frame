using Modules.Sync.Contracts;

namespace Modules.Sync.Replica;

/// <summary>
/// 快照单调应用机：客户端唯一的状态推进入口。
/// <para>
/// 判据（R2）：仅接受 <c>tick &gt; lastAppliedTick</c> 的快照；
/// 同 tick 重复到达丢弃；旧 tick（乱序回退）丢弃。首帧没有水位，直接接受（不插值、不补帧）。
/// 丢弃判定只按 tick 单调，<b>绝不按哈希</b>；哈希只在同 tick 内比对。
/// </para>
/// <para>
/// 依赖点：契约 <see cref="Snapshot"/> / <see cref="SyncStateProjection"/> / <see cref="StateHash"/>
/// 已就位；传输（<see cref="ISyncTransport"/>）与观测（<see cref="SyncObserver"/>）由
/// <see cref="SyncClientReplica"/> 持有，本类不起线程、不排队。
/// </para>
/// </summary>
public sealed class SnapshotGuard
{
    private readonly ReplicaTickLedger _ledger;
    private long _lastAppliedTick = -1L;
    private bool _hasApplied;
    private long _appliedCount;
    private StateHash _lastAppliedHash;

    public SnapshotGuard(ReplicaTickLedger? ledger = null)
    {
        _ledger = ledger ?? new ReplicaTickLedger();
    }

    /// <summary>逐 tick 处置台账（只读取证面）。</summary>
    public ReplicaTickLedger Ledger => _ledger;

    /// <summary>当前已应用水位；只由被接受的快照推进。</summary>
    public AppliedState State => new(_hasApplied, _lastAppliedTick, _appliedCount, _lastAppliedHash);

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

    /// <summary>拒绝路径：只写台账与计数，水位不动（用于旧/重复/哈希不一致）。</summary>
    internal void RecordSkipped(long snapshotTick, ReplicaTickOutcome outcome)
        => _ledger.Record(snapshotTick, outcome, 0L, default);

    /// <summary>
    /// 提交路径：仅在 <see cref="Decide"/> 返回 <see cref="ReplicaTickOutcome.Applied"/>
    /// 且同 tick 哈希校验通过后调用；推进水位并记应用序号。
    /// </summary>
    internal void CommitApplied(long snapshotTick, StateHash hash)
    {
        if (Decide(snapshotTick) != ReplicaTickOutcome.Applied)
        {
            throw new InvalidOperationException(
                $"tick {snapshotTick} 未通过单调判据，不允许提交应用。");
        }

        _lastAppliedTick = snapshotTick;
        _hasApplied = true;
        _appliedCount++;
        _lastAppliedHash = hash;
        _ledger.Record(snapshotTick, ReplicaTickOutcome.Applied, _appliedCount, hash);
    }
}
