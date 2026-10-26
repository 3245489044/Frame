#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace Share.StateManagement;

/// <summary>
/// 版本化快照缓存（唯一实现）：键 = (owner, 版本)，TTL 仅兜底漏事件。
/// <para>
/// 不变量：版本不一致 ⇒ 本拍不写/不命中（禁混合快照）；命中标记按 owner 记录（多宿主遥测不串源，
/// 进程级汇总语义已删除）。
/// </para>
/// </summary>
public sealed class VersionedSnapshotCache<TValue> where TValue : class
{
    private sealed class Entry
    {
        public long Version;
        public long StoredAt;
        public bool Hit;
        public TValue? Value;
    }

    private readonly ConditionalWeakTable<object, Entry> _entries = new();
    private readonly long _ttlMs;

    public VersionedSnapshotCache(long ttlMs)
        => _ttlMs = ttlMs < 0 ? 0 : ttlMs;

    /// <summary>指定 owner 最近一次读取是否命中（无 owner 级汇总出口）。</summary>
    public bool LastHit(object owner)
        => owner is not null && _entries.TryGetValue(owner, out var entry) && entry.Hit;

    /// <summary>按 (owner, 版本) 取快照；版本不符或超过 TTL ⇒ 未命中。</summary>
    public bool TryGet(object owner, long version, out TValue? value)
    {
        if (owner is not null && _entries.TryGetValue(owner, out var entry))
        {
            var hit = entry.Version == version
                      && Environment.TickCount64 - entry.StoredAt <= _ttlMs;
            entry.Hit = hit;
            if (hit)
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>写入快照（版本 + 落盘时刻）；刚重建的条目命中标记 = false（本拍归因"已重建"）。</summary>
    public void Put(object owner, long version, TValue value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(value);
        var entry = _entries.GetOrCreateValue(owner);
        entry.Version = version;
        entry.StoredAt = Environment.TickCount64;
        entry.Hit = false;
        entry.Value = value;
    }
}
