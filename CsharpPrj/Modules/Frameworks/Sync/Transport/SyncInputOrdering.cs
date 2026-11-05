using System.Collections.Generic;
using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>
/// 输入排序机：把已通过窗口判定的 <see cref="TakeoverCommand"/> 按目标 tick 暂存（窗口内“未来 tick 排队”），
/// 到 tick 时按契约固定全序 <c>TargetTick → Kind → CommandId</c> 吐出，并对同 <c>(tick, 槽位)</c> 实现“首条优先”。
/// <para>“首条” = 上述全序中的最小者，<b>不是最先到达者</b>：同一集合无论以什么顺序到达，每个槽位的胜者都相同。
/// <see cref="Kind"/> 的序号即槽位键（契约已声明“序号即同 tick 内的次级排序键”）。</para>
/// <para>容量有界：桶数组固定为 <c>InputWindowTicks + 1</c>，环形复用，滑出即回收。</para>
/// </summary>
public sealed class SyncInputOrdering
{
    private readonly SlotBucket[] _buckets;
    private readonly long[] _consumedTicks;

    /// <param name="windowTicks">窗口宽度（含端点）；缺省取契约的 <see cref="SyncProtocol.InputWindowTicks"/>。</param>
    public SyncInputOrdering(int windowTicks = SyncProtocol.InputWindowTicks)
    {
        if (windowTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowTicks), "窗口宽度不能为负。");
        }

        _buckets = new SlotBucket[windowTicks + 1];
        for (var i = 0; i < _buckets.Length; i++)
        {
            _buckets[i] = new SlotBucket();
        }

        _consumedTicks = new long[windowTicks + 1];
        Array.Fill(_consumedTicks, long.MinValue);
    }

    /// <summary>该 tick 是否已被 <see cref="DrainTick"/> 消费。</summary>
    public bool IsTickConsumed(long tick) => _consumedTicks[SlotOf(tick, _consumedTicks.Length)] == tick;

    /// <summary>
    /// 预留一个排序位。返回 false = 本次落败（该 tick 已消费，或同槽位已有确定性序更小的命令）。
    /// 返回 true 且 <paramref name="evicted"/> 非空 = 本次更小，称王并淘汰了先前预留者。
    /// </summary>
    public bool TryReserve(TakeoverCommand command, out TakeoverCommand? evicted)
    {
        ArgumentNullException.ThrowIfNull(command);

        evicted = null;
        if (IsTickConsumed(command.TargetTick))
        {
            return false;
        }

        var bucket = BucketFor(command.TargetTick);
        var slot = (int)command.Kind;
        if (bucket.Slots.TryGetValue(slot, out var existing))
        {
            if (Compare(command, existing) >= 0)
            {
                return false;
            }

            evicted = existing;
        }

        bucket.Slots[slot] = command;
        return true;
    }

    /// <summary>
    /// 消费（drain）某 tick：按 <c>TargetTick → Kind → CommandId</c> 全序写入 <paramref name="sink"/>，
    /// 并把该 tick 标记为已消费。输出条数 = 该 tick 实际槽位数；<paramref name="sink"/> 不足会抛异常而不是截断。
    /// </summary>
    public int DrainTick(long tick, TakeoverCommand[] sink)
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
        foreach (var command in bucket.Slots.Values)
        {
            sink[index++] = command;
        }

        InsertionSort(sink, count);
        bucket.Slots.Clear();
        return count;
    }

    /// <summary>推进当前 tick：回收已滑出窗口且从未被 drain 的桶（这些条目即“太旧丢弃”）。</summary>
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

    /// <summary>同 tick 首条优先的确定性全序：<c>TargetTick ↑ → Kind 序号 ↑ → CommandId ↑</c>。</summary>
    private static int Compare(TakeoverCommand left, TakeoverCommand right)
    {
        var byTick = left.TargetTick.CompareTo(right.TargetTick);
        if (byTick != 0)
        {
            return byTick;
        }

        var byKind = ((int)left.Kind).CompareTo((int)right.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        return left.CommandId.CompareTo(right.CommandId);
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

    private static void InsertionSort(TakeoverCommand[] commands, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var current = commands[i];
            var j = i - 1;
            while (j >= 0 && Compare(commands[j], current) > 0)
            {
                commands[j + 1] = commands[j];
                j--;
            }

            commands[j + 1] = current;
        }
    }

    private sealed class SlotBucket
    {
        public long Tick = long.MinValue;
        public readonly Dictionary<int, TakeoverCommand> Slots = new();
    }
}
