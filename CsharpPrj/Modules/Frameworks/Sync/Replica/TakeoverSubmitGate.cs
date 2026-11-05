using Modules.Sync.Contracts;

namespace Modules.Sync.Replica;

/// <summary>
/// 手动接管上送的窗口判据结果（副本本地判据枚举，不是线格式消息类型）。
/// </summary>
public enum TakeoverSubmitOutcome
{
    /// <summary>落在输入窗口内且未重复提交，允许上送。</summary>
    Accepted = 0,

    /// <summary>尚未应用任何快照：没有时间基准，无法给出合法 <c>TargetTick</c>。</summary>
    RejectedNoTimeBase = 1,

    /// <summary><c>TargetTick &lt; current</c>：迟到。</summary>
    RejectedLate = 2,

    /// <summary><c>TargetTick &gt; current + window</c>：超前瞻。</summary>
    RejectedFuture = 3,

    /// <summary>同一 <c>(TargetTick, 幂等键)</c> 已提交过：幂等丢弃，不重发。</summary>
    RejectedDuplicate = 4,
}

/// <summary>
/// 手动接管（MVP 仅「预判保护」与「超频」两项）的上送窗口闸门。
/// <para>
/// 只做两件事：判定 <c>TargetTick</c> 是否落在 <c>[current, current + window]</c>，
/// 以及同一 <c>(TargetTick, 幂等键)</c> 是否重复提交。不做本地预测、不做回滚，
/// 也不预演权威端会如何裁决——结果只由后续快照决定。
/// </para>
/// <para>
/// 已接契约：窗口宽度默认取 <see cref="SyncProtocol.InputWindowTicks"/>；
/// 幂等键就是 <see cref="TakeoverCommand"/> 的 <c>CommandId</c>（int64 单调序号，重试复用）。
/// 本类只判定窗口与重复，不解释 <see cref="TakeoverKind"/> 语义。
/// </para>
/// </summary>
public sealed class TakeoverSubmitGate
{
    /// <summary>窗口内允许缓存的待判定命令上限，超出即淘汰最旧，保持有界。</summary>
    public const int DefaultMaxPending = 32;

    private readonly long _windowTicks;
    private readonly int _maxPending;
    private readonly List<SubmittedKey> _pending = new();
    private long _nextSequence;
    private long _accepted;
    private long _rejectedLate;
    private long _rejectedFuture;
    private long _rejectedDuplicate;
    private long _rejectedNoTimeBase;

    public TakeoverSubmitGate(long windowTicks = SyncProtocol.InputWindowTicks, int maxPending = DefaultMaxPending)
    {
        if (windowTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(windowTicks));
        if (maxPending < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPending));

        _windowTicks = windowTicks;
        _maxPending = maxPending;
    }

    /// <summary>输入窗口宽度（决策 tick）。</summary>
    public long WindowTicks => _windowTicks;

    public long AcceptedCount => _accepted;

    public long RejectedLateCount => _rejectedLate;

    public long RejectedFutureCount => _rejectedFuture;

    public long RejectedDuplicateCount => _rejectedDuplicate;

    public long RejectedNoTimeBaseCount => _rejectedNoTimeBase;

    /// <summary>
    /// 分配客户端本地单调 <c>CommandId</c>（1 起，只增不减）。
    /// 重试同一逻辑命令时必须复用已有序号，不得重新分配；禁止随机 id。
    /// </summary>
    public long NextCommandSequence() => ++_nextSequence;

    /// <summary>
    /// 以已应用水位为时间基准判定并登记一条接管上送。
    /// </summary>
    public TakeoverSubmitOutcome TrySubmit(AppliedState state, long targetTick, long commandId)
        => TrySubmit(state.HasApplied, state.LastAppliedTick, targetTick, commandId);

    /// <summary>
    /// 判定并登记一条接管上送。
    /// </summary>
    /// <param name="hasTimeBase">客户端是否已应用过快照（来自 <see cref="AppliedState.HasApplied"/>）。</param>
    /// <param name="currentTick">当前水位（来自 <see cref="AppliedState.LastAppliedTick"/>）。</param>
    /// <param name="targetTick">命令期望生效的决策 tick。</param>
    /// <param name="commandId">幂等键（<see cref="TakeoverCommand.CommandId"/>）：同一逻辑命令必须复用同一 id。</param>
    public TakeoverSubmitOutcome TrySubmit(bool hasTimeBase, long currentTick, long targetTick, long commandId)
    {
        if (!hasTimeBase)
        {
            _rejectedNoTimeBase++;
            return TakeoverSubmitOutcome.RejectedNoTimeBase;
        }

        Prune(currentTick);

        if (Contains(targetTick, commandId))
        {
            _rejectedDuplicate++;
            return TakeoverSubmitOutcome.RejectedDuplicate;
        }

        if (targetTick < currentTick)
        {
            _rejectedLate++;
            return TakeoverSubmitOutcome.RejectedLate;
        }

        if (targetTick > currentTick + _windowTicks)
        {
            _rejectedFuture++;
            return TakeoverSubmitOutcome.RejectedFuture;
        }

        _pending.Add(new SubmittedKey(targetTick, commandId));
        EnforceBound();
        _accepted++;
        return TakeoverSubmitOutcome.Accepted;
    }

    /// <summary>当前窗口内已登记（待判定）的命令条数。</summary>
    public int PendingCount => _pending.Count;

    private bool Contains(long targetTick, long commandId)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            SubmittedKey key = _pending[i];
            if (key.TargetTick == targetTick && key.CommandId == commandId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 回收已滑出窗口的登记项：<c>TargetTick &lt; current</c> 的命令不可能再进入窗口，
    /// 保留它们只会让幂等表无界增长。
    /// </summary>
    private void Prune(long currentTick)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].TargetTick < currentTick)
                _pending.RemoveAt(i);
        }
    }

    private void EnforceBound()
    {
        while (_pending.Count > _maxPending)
            _pending.RemoveAt(0);
    }

    private readonly struct SubmittedKey
    {
        internal SubmittedKey(long targetTick, long commandId)
        {
            TargetTick = targetTick;
            CommandId = commandId;
        }

        internal long TargetTick { get; }

        internal long CommandId { get; }
    }
}
