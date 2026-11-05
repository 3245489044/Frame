using System;
using System.Collections.Generic;

namespace Modules.Sync.Transport;

/// <summary>
/// 幂等键窗口：按 <c>(决策 tick, commandId)</c> 记录“已受理”项，重复到达只认一次。
/// <para>有界回收：桶数组固定为 <c>window + 1</c> 个，tick 滑出窗口后复用桶并清空，
/// <b>不存在</b>按 tick 无限增长的字典；内存上界 = <c>(window + 1) × 单 tick 已受理命令数</c>。</para>
/// <para>键的稳定性由调用方保证：同一逻辑命令在任何重试/重放路径下必须给出同一 <c>commandId</c> 文本；
/// 本类不生成键、不做哈希、不做随机化。</para>
/// <para>[契约依赖 · 等待 SY-00] <c>CommandId</c> 的线上类型由契约决定；本泛型类不选定类型，
/// 会话层以稳定字符串实例化。</para>
/// </summary>
/// <typeparam name="TCommandId">幂等键标识（契约落地后可为契约字段类型或适配后的稳定文本）。</typeparam>
public sealed class SyncIdempotencyWindow<TCommandId> where TCommandId : notnull
{
    private readonly long[] _stamps;
    private readonly HashSet<TCommandId>[] _sets;

    /// <param name="windowTicks">窗口宽度（含端点）；契约落地后由 <c>InputWindowTicks</c> 传入。</param>
    /// <param name="initialTick">初始决策 tick，用于占位（不预登记任何键）。</param>
    public SyncIdempotencyWindow(int windowTicks, long initialTick)
    {
        if (windowTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowTicks), "窗口宽度不能为负。");
        }

        if (initialTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTick), "决策 tick 不能为负。");
        }

        _stamps = new long[windowTicks + 1];
        Array.Fill(_stamps, long.MinValue);
        _sets = new HashSet<TCommandId>[windowTicks + 1];
        for (var i = 0; i < _sets.Length; i++)
        {
            _sets[i] = new HashSet<TCommandId>();
        }
    }

    /// <summary>是否已登记（只查询，不写入）。</summary>
    public bool IsRegistered(long tick, TCommandId commandId)
    {
        var slot = SlotOf(tick, _stamps.Length);
        return _stamps[slot] == tick && _sets[slot].Contains(commandId);
    }

    /// <summary>登记一条已受理命令；返回 false 表示重复到达（<b>不覆盖</b>、不产生第二次应用）。</summary>
    public bool TryRegister(long tick, TCommandId commandId)
    {
        return SetFor(tick).Add(commandId);
    }

    /// <summary>
    /// 撤销登记：仅用于排序机淘汰（同一槽位出现确定性序更小的键时，被淘汰者从未真正应用，
    /// 不能继续占用幂等名额，否则重试会被误判为重复）。
    /// </summary>
    public bool Revoke(long tick, TCommandId commandId)
    {
        var slot = SlotOf(tick, _stamps.Length);
        return _stamps[slot] == tick && _sets[slot].Remove(commandId);
    }

    /// <summary>推进当前 tick：把已滑出窗口的桶整体清空回收（不是逐条删除）。</summary>
    public void AdvanceTo(long currentTick)
    {
        for (var i = 0; i < _stamps.Length; i++)
        {
            if (_stamps[i] < currentTick)
            {
                _sets[i].Clear();
                _stamps[i] = long.MinValue;
            }
        }
    }

    private HashSet<TCommandId> SetFor(long tick)
    {
        var slot = SlotOf(tick, _stamps.Length);
        if (_stamps[slot] != tick)
        {
            _stamps[slot] = tick;
            _sets[slot].Clear();
        }

        return _sets[slot];
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
}
