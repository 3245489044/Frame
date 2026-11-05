using Modules.Sync.Contracts;

namespace Modules.Sync.Replica;

/// <summary>
/// 一条快照到达后的处置结果（副本本地判据枚举，不是线格式消息类型）。
/// </summary>
public enum ReplicaTickOutcome
{
    /// <summary>已应用的正常快照。</summary>
    Applied = 0,

    /// <summary>与当前水位同 tick：视为重复到达，丢弃（含内容不一致的同 tick 快照）。</summary>
    SkippedDuplicateTick = 1,

    /// <summary>tick 小于当前水位：乱序回退的旧快照，丢弃。</summary>
    SkippedStaleTick = 2,

    /// <summary>tick 判据通过，但重算的 <see cref="StateHash"/> 与权威值不同：不应用（水位不推进）。</summary>
    SkippedHashMismatch = 3,
}

/// <summary>
/// 单个 tick 的处置记录：为后续「双端一致性断言」保留可取证面。
/// </summary>
public readonly struct ReplicaTickRecord
{
    public ReplicaTickRecord(long tick, ReplicaTickOutcome outcome, long appliedOrdinal, StateHash hash)
    {
        Tick = tick;
        Outcome = outcome;
        AppliedOrdinal = appliedOrdinal;
        Hash = hash;
    }

    /// <summary>本条快照携带的决策 tick。</summary>
    public long Tick { get; }

    /// <summary>接受还是跳过、以何种理由跳过。</summary>
    public ReplicaTickOutcome Outcome { get; }

    /// <summary>已应用序号（1 起）；跳过时为 0。</summary>
    public long AppliedOrdinal { get; }

    /// <summary>被应用快照的状态哈希；跳过时为零值（未计算）。</summary>
    public StateHash Hash { get; }
}

/// <summary>
/// 定容环形台账：逐 tick 记录「应用了什么、跳过什么」。
/// <para>
/// 只保留最近 <see cref="DefaultCapacity"/> 条处置记录（环形覆盖），不缓存超出窗口的历史；
/// 汇总计数长期保留，供 <c>client==server</c> / <c>stale</c> / <c>hashMismatch</c> 类断言取证。
/// </para>
/// </summary>
public sealed class ReplicaTickLedger
{
    /// <summary>默认容量：覆盖最近窗口，同时保持有界内存。</summary>
    public const int DefaultCapacity = 256;

    private readonly ReplicaTickRecord[] _entries;
    private int _start;
    private int _count;
    private long _applied;
    private long _skippedDuplicate;
    private long _skippedStale;
    private long _skippedHashMismatch;

    public ReplicaTickLedger(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _entries = new ReplicaTickRecord[capacity];
    }

    /// <summary>台账容量（环形上限）。</summary>
    public int Capacity => _entries.Length;

    /// <summary>当前保留的处置记录条数。</summary>
    public int Count => _count;

    public long AppliedCount => _applied;

    public long SkippedDuplicateCount => _skippedDuplicate;

    public long SkippedStaleCount => _skippedStale;

    /// <summary>哈希不一致丢弃次数，对应契约指标 <c>sync.hashMismatch</c> 的计数口径。</summary>
    public long SkippedHashMismatchCount => _skippedHashMismatch;

    /// <summary>旧/重复跳过合计，对应契约指标 <c>sync.snapshot.stale</c> 的计数口径。</summary>
    public long SkippedCount => _skippedDuplicate + _skippedStale;

    /// <summary>写入一条处置记录；由 <see cref="SnapshotGuard"/> 调用，外部只读。</summary>
    internal void Record(long tick, ReplicaTickOutcome outcome, long appliedOrdinal, StateHash hash)
    {
        switch (outcome)
        {
            case ReplicaTickOutcome.Applied:
                _applied++;
                break;
            case ReplicaTickOutcome.SkippedDuplicateTick:
                _skippedDuplicate++;
                break;
            case ReplicaTickOutcome.SkippedStaleTick:
                _skippedStale++;
                break;
            case ReplicaTickOutcome.SkippedHashMismatch:
                _skippedHashMismatch++;
                break;
        }

        var record = new ReplicaTickRecord(tick, outcome, appliedOrdinal, hash);

        if (_count == _entries.Length)
        {
            // 环满：覆盖最旧一条，起点后移。
            _entries[_start] = record;
            _start = (_start + 1) % _entries.Length;
            return;
        }

        _entries[(_start + _count) % _entries.Length] = record;
        _count++;
    }

    /// <summary>按时间顺序（旧→新）导出一份台账快照，供断言读取；不改变台账状态。</summary>
    public ReplicaTickLedgerSnapshot Capture()
    {
        var entries = new ReplicaTickRecord[_count];
        for (int i = 0; i < _count; i++)
            entries[i] = _entries[(_start + i) % _entries.Length];

        return new ReplicaTickLedgerSnapshot(
            _applied,
            _skippedDuplicate,
            _skippedStale,
            _skippedHashMismatch,
            entries);
    }

    /// <summary>清空台账（仅用于新建会话后的重新取证，不是回滚）。</summary>
    public void Clear()
    {
        _start = 0;
        _count = 0;
        _applied = 0;
        _skippedDuplicate = 0;
        _skippedStale = 0;
        _skippedHashMismatch = 0;
    }
}

/// <summary>台账的只读取证快照。</summary>
public readonly struct ReplicaTickLedgerSnapshot
{
    private readonly ReplicaTickRecord[] _entries;

    internal ReplicaTickLedgerSnapshot(
        long applied,
        long skippedDuplicate,
        long skippedStale,
        long skippedHashMismatch,
        ReplicaTickRecord[] entries)
    {
        AppliedCount = applied;
        SkippedDuplicateCount = skippedDuplicate;
        SkippedStaleCount = skippedStale;
        SkippedHashMismatchCount = skippedHashMismatch;
        _entries = entries;
    }

    public long AppliedCount { get; }

    public long SkippedDuplicateCount { get; }

    public long SkippedStaleCount { get; }

    public long SkippedHashMismatchCount { get; }

    /// <summary>旧/重复跳过合计（<c>sync.snapshot.stale</c> 口径）。</summary>
    public long SkippedCount => SkippedDuplicateCount + SkippedStaleCount;

    /// <summary>保留的处置记录条数。</summary>
    public int Count => _entries?.Length ?? 0;

    /// <summary>按下标取处置记录（0 为最旧）。</summary>
    public ReplicaTickRecord this[int index] => _entries[index];

    /// <summary>复制一段处置记录（旧→新），便于断言逐条比对。</summary>
    public ReplicaTickRecord[] ToArray() => _entries is null ? [] : (ReplicaTickRecord[])_entries.Clone();
}
