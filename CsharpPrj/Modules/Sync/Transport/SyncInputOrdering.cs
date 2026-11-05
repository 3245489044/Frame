using System;
using System.Collections.Generic;

namespace Modules.Sync.Transport;

/// <summary>
/// 同 tick 多输入的确定性排序键。<b>顺序与到达时刻无关</b>，只由三个字段决定：
/// <c>TargetTick 升序 → SlotOrdinal 升序 → CommandId 序数（StringComparison.Ordinal）升序</c>。
/// <para>“首条优先”里的“首条”= 上述全序中的最小者，不是最先到达者；因此同一集合无论以什么顺序到达，
/// 每个 <c>(tick, 槽位)</c> 的胜者都相同。</para>
/// <para>[契约依赖 · 等待 SY-00] <c>CommandId</c> 的线上类型由契约决定；本机以稳定字符串键参与比较
/// （适配层负责把契约值规范化为稳定文本），本类不定义消息类型。</para>
/// </summary>
public readonly struct SyncInputKey : IComparable<SyncInputKey>, IEquatable<SyncInputKey>
{
    public SyncInputKey(long targetTick, int slotOrdinal, string commandId)
    {
        if (slotOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotOrdinal), "槽位序号不能为负。");
        }

        TargetTick = targetTick;
        SlotOrdinal = slotOrdinal;
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
    }

    /// <summary>目标决策 tick。</summary>
    public long TargetTick { get; }

    /// <summary>槽位序号（由契约的 <c>Kind</c> 映射得到；本层不定义 <c>Kind</c> 枚举）。</summary>
    public int SlotOrdinal { get; }

    /// <summary>命令幂等键的稳定文本。</summary>
    public string CommandId { get; }

    /// <inheritdoc />
    public int CompareTo(SyncInputKey other)
    {
        var byTick = TargetTick.CompareTo(other.TargetTick);
        if (byTick != 0)
        {
            return byTick;
        }

        var bySlot = SlotOrdinal.CompareTo(other.SlotOrdinal);
        if (bySlot != 0)
        {
            return bySlot;
        }

        return string.CompareOrdinal(CommandId, other.CommandId);
    }

    /// <inheritdoc />
    public bool Equals(SyncInputKey other) => CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SyncInputKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TargetTick, SlotOrdinal, CommandId);

    /// <inheritdoc />
    public override string ToString() => $"tick={TargetTick},slot={SlotOrdinal},cmd={CommandId}";
}

/// <summary>
/// 输入排序机：把已通过窗口判定的输入按目标 tick 暂存（窗口内“未来 tick 排队”），
/// 到 tick 时按确定性全序吐出，并对同 <c>(tick, 槽位)</c> 实现“首条优先、不做后写覆盖”。
/// <para>容量有界：桶数组长度固定为 <c>window + 1</c>（环形复用，滑出即回收）；每个桶的槽位表规模由槽位数决定。</para>
/// <para>淘汰规则：若同槽位已有确定性序更小的键，新键被丢弃（<see cref="TryReserve"/> 返回 false）；
/// 若新键更小，则以新键为准并回报被淘汰者，供调用方撤销其幂等登记——最终胜者只由全序决定，与到达先后无关。</para>
/// </summary>
public sealed class SyncInputOrdering
{
    private readonly int _windowTicks;
    private readonly SlotBucket[] _buckets;
    private readonly long[] _consumedTicks;

    /// <param name="windowTicks">窗口宽度（含端点）；契约落地后由 <c>InputWindowTicks</c> 传入。</param>
    public SyncInputOrdering(int windowTicks)
    {
        if (windowTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowTicks), "窗口宽度不能为负。");
        }

        _windowTicks = windowTicks;
        _buckets = new SlotBucket[windowTicks + 1];
        for (var i = 0; i < _buckets.Length; i++)
        {
            _buckets[i] = new SlotBucket();
        }

        _consumedTicks = new long[windowTicks + 1];
        Array.Fill(_consumedTicks, long.MinValue);
    }

    /// <summary>该 tick 是否已被 <see cref="DrainTick"/> 消费（消费后同 tick 的新输入一律拒绝）。</summary>
    public bool IsTickConsumed(long tick) => _consumedTicks[SlotOf(tick, _consumedTicks.Length)] == tick;

    /// <summary>
    /// 预留一个排序位。返回 false = 本次落败（该 tick 已消费，或同槽位已有确定性序更小的键）。
    /// 返回 true 且 <paramref name="evicted"/> 非空 = 本次更小，称王并淘汰了旧键。
    /// </summary>
    public bool TryReserve(SyncInputKey key, out SyncInputKey? evicted)
    {
        evicted = null;

        if (IsTickConsumed(key.TargetTick))
        {
            return false;
        }

        var bucket = BucketFor(key.TargetTick);
        if (bucket.Slots.TryGetValue(key.SlotOrdinal, out var existing))
        {
            if (key.CompareTo(existing) >= 0)
            {
                return false;
            }

            evicted = existing;
        }

        bucket.Slots[key.SlotOrdinal] = key;
        return true;
    }

    /// <summary>
    /// 消费（drain）某 tick 的全部预留：按确定性全序写入 <paramref name="sink"/>，并把该 tick 标记为已消费。
    /// 输出条数 = 该 tick 实际槽位数；<paramref name="sink"/> 不足会抛异常而不是截断。
    /// </summary>
    public int DrainTick(long tick, SyncInputKey[] sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        MarkConsumed(tick);
        var bucket = BucketFor(tick);
        var count = bucket.Slots.Count;
        if (count == 0)
        {
            return 0;
        }

        if (count > sink.Length)
        {
            throw new ArgumentException("接收缓冲小于该 tick 的槽位数，拒绝截断输出。", nameof(sink));
        }

        var index = 0;
        foreach (var key in bucket.Slots.Values)
        {
            sink[index++] = key;
        }

        InsertionSort(sink, count);
        bucket.Slots.Clear();
        return count;
    }

    /// <summary>推进当前 tick：回收已滑出窗口、且从未被 drain 的桶（这些条目即“太旧丢弃”）。</summary>
    public void AdvanceTo(long currentTick)
    {
        foreach (var bucket in _buckets)
        {
            if (bucket.Slots.Count != 0 && bucket.Tick < currentTick)
            {
                bucket.Slots.Clear();
                bucket.Tick = long.MinValue;
            }
        }
    }

    private SlotBucket BucketFor(long tick)
    {
        var bucket = _buckets[SlotOf(tick, _buckets.Length)];
        if (bucket.Tick != tick)
        {
            bucket.Tick = tick;
            bucket.Slots.Clear();
        }

        return bucket;
    }

    private void MarkConsumed(long tick)
    {
        _consumedTicks[SlotOf(tick, _consumedTicks.Length)] = tick;
    }

    private static int SlotOf(long tick, int size)
    {
        var modulo = tick % size;
        if (modulo < 0)
        {
            modulo += size;
        }

        return (int)modulo;
    }

    private static void InsertionSort(SyncInputKey[] keys, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var current = keys[i];
            var j = i - 1;
            while (j >= 0 && keys[j].CompareTo(current) > 0)
            {
                keys[j + 1] = keys[j];
                j--;
            }

            keys[j + 1] = current;
        }
    }

    private sealed class SlotBucket
    {
        public long Tick = long.MinValue;
        public readonly Dictionary<int, SyncInputKey> Slots = new();
    }
}
