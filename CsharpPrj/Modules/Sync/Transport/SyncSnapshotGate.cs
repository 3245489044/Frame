using System;

namespace Modules.Sync.Transport;

/// <summary>快照处置裁决策：每个值都是一个“为什么丢掉这一条”的可枚举答案。</summary>
public enum SyncSnapshotVerdict
{
    /// <summary>接受：<c>tick &gt; lastAppliedTick</c>，单调前进。</summary>
    Accepted = 0,

    /// <summary>非法 tick：负值（决策 tick 从 0 起），不进状态、不计为正常旧快照。</summary>
    DiscardedInvalidTick = 1,

    /// <summary>旧快照：<c>tick &lt; lastAppliedTick</c>，会造成状态回退。</summary>
    DiscardedRegressed = 2,

    /// <summary>重复快照：<c>tick == lastAppliedTick</c>，该 tick 已应用（同 tick 只认一条）。</summary>
    DiscardedRepeated = 3,
}

/// <summary>
/// 快照单调门（R1/R2 的落地）：只接受 <c>tick &gt; lastAppliedTick</c> 的快照；相等=重复、更小=旧快照、负值=非法。
/// <para>只维护“已应用到第几 tick”，不保存投影、不计算哈希、不比较字节——那些属于契约。
/// 返回的 <see cref="SyncSnapshotVerdict"/> 即丢弃判据，可逐条回答“为什么丢掉”。</para>
/// <para>单写者模型：由副本的接收线程独占；不内部加锁。</para>
/// </summary>
public sealed class SyncSnapshotGate
{
    private long _lastAppliedTick;

    /// <param name="initialLastAppliedTick">起始“已应用”位置；决策 tick 从 0 起，故缺省 -1 表示尚未应用任何快照。</param>
    public SyncSnapshotGate(long initialLastAppliedTick = -1)
    {
        if (initialLastAppliedTick < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(initialLastAppliedTick), "起始位置不能小于 -1。");
        }

        _lastAppliedTick = initialLastAppliedTick;
    }

    /// <summary>最近一次成功应用的决策 tick（未应用过为 -1）。</summary>
    public long LastAppliedTick => _lastAppliedTick;

    /// <summary>判定并（在接受时）推进已应用位置。</summary>
    public SyncSnapshotVerdict TryAccept(long tick)
    {
        if (tick < 0)
        {
            return SyncSnapshotVerdict.DiscardedInvalidTick;
        }

        if (tick == _lastAppliedTick)
        {
            return SyncSnapshotVerdict.DiscardedRepeated;
        }

        if (tick < _lastAppliedTick)
        {
            return SyncSnapshotVerdict.DiscardedRegressed;
        }

        _lastAppliedTick = tick;
        return SyncSnapshotVerdict.Accepted;
    }
}
