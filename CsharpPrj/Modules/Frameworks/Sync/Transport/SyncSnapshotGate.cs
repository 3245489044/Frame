using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>
/// 快照单调门（R1/R2 在客户端侧的落地）：只接受 <c>Tick &gt; lastAppliedTick</c> 的快照。
/// <para>丢弃判据全部用契约 <see cref="RejectReason"/> 表达，可逐条回答“为什么丢掉这一条”：
/// 信封与投影 tick 不一致/负 tick → <see cref="RejectReason.Malformed"/>；
/// 同 tick 再来一次 → <see cref="RejectReason.Duplicate"/>；比已应用更旧 → <see cref="RejectReason.Late"/>。</para>
/// <para><b>哈希口径：</b><see cref="StateHash"/> 的输入域含 <c>Tick</c>，故本层只校验“信封 tick == 投影 tick”
/// 这一个同 tick 前提，<b>不跨 tick 比较哈希</b>；真实的 <c>AppliedHash</c> 对账属于副本在同一 tick 内的职责。</para>
/// <para>单写者模型：由副本接收线程独占；不内部加锁。</para>
/// </summary>
public sealed class SyncSnapshotGate
{
    private readonly SyncObserver _observer;
    private long _lastAppliedTick;

    /// <param name="observer">丢弃时回调 <see cref="SyncObserver.OnSnapshotDiscarded"/>；缺省空观察者。</param>
    /// <param name="initialLastAppliedTick">起始“已应用”位置；决策 tick 从 0 起，故缺省 -1 表示尚未应用任何快照。</param>
    public SyncSnapshotGate(SyncObserver? observer = null, long initialLastAppliedTick = -1)
    {
        if (initialLastAppliedTick < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(initialLastAppliedTick), "起始位置不能小于 -1。");
        }

        _observer = observer ?? new NoopSyncObserver();
        _lastAppliedTick = initialLastAppliedTick;
    }

    /// <summary>最近一次成功应用的决策 tick（未应用过为 -1）。</summary>
    public long LastAppliedTick => _lastAppliedTick;

    /// <summary>判定并（在接受时）推进已应用位置；返回拒绝原因，<c>null</c> 表示已接受。</summary>
    public RejectReason? TryAccept(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var tick = snapshot.Tick;
        var reason = Classify(snapshot, tick);
        if (reason is not null)
        {
            _observer.OnSnapshotDiscarded(tick);
            return reason;
        }

        _lastAppliedTick = tick;
        return null;
    }

    private RejectReason? Classify(Snapshot snapshot, long tick)
    {
        if (tick < 0 || snapshot.Projection is null || snapshot.Projection.Tick != tick)
        {
            return RejectReason.Malformed;
        }

        if (tick == _lastAppliedTick)
        {
            return RejectReason.Duplicate;
        }

        if (tick < _lastAppliedTick)
        {
            return RejectReason.Late;
        }

        return null;
    }
}
