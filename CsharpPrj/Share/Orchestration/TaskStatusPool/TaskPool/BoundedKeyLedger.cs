using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 有界键账（通用）：进程内「键 → 值」派生账，按写入序 FIFO 淘汰，防长跑无界增长。
/// <para>定位：观测/幂等缓存，不持久化、不参与判定（真源仍在持久化）。同构实现原先散落在
/// 任务编排幂等账 / 观测账 / 快照账三处，此类型为单一实现。</para>
/// <para>并发语义：<see cref="TryRegister"/> 原子去重；淘汰在写入路径上同步执行，允许「并发写入 + 淘汰」交错
/// （最坏情况多淘汰一条，不会丢键语义）。</para>
/// </summary>
public sealed class BoundedKeyLedger<TValue>
    where TValue : notnull
{
    private readonly ConcurrentDictionary<string, TValue> _map;
    private readonly ConcurrentQueue<string> _order = new();
    private readonly int _capacity;

    public BoundedKeyLedger(int capacity = 8192, IEqualityComparer<string>? comparer = null)
    {
        _capacity = capacity <= 0 ? 8192 : capacity;
        _map = new ConcurrentDictionary<string, TValue>(comparer ?? StringComparer.Ordinal);
    }

    /// <summary>保留上限（溢出按写入序淘汰）。</summary>
    public int Capacity => _capacity;

    public int Count => _map.Count;

    /// <summary>注册键（已存在 ⇒ false；成功后必要时按 FIFO 淘汰）。</summary>
    public bool TryRegister(string key, TValue value)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = key.Trim();
        if (!_map.TryAdd(normalized, value)) return false;

        _order.Enqueue(normalized);
        Trim();
        return true;
    }

    /// <summary>写入或覆盖（新键入淘汰队列）。</summary>
    public void Set(string key, TValue value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var normalized = key.Trim();
        var added = !_map.ContainsKey(normalized);
        _map[normalized] = value;
        if (added) _order.Enqueue(normalized);
        Trim();
    }

    public bool Contains(string? key)
        => !string.IsNullOrWhiteSpace(key) && _map.ContainsKey(key.Trim());

    public bool TryGet(string? key, out TValue value)
    {
        value = default!;
        return !string.IsNullOrWhiteSpace(key) && _map.TryGetValue(key.Trim(), out value!);
    }

    public bool Remove(string? key)
        => !string.IsNullOrWhiteSpace(key) && _map.TryRemove(key.Trim(), out _);

    public IReadOnlyList<string> Keys() => new List<string>(_map.Keys);

    public void Clear()
    {
        _map.Clear();
        while (_order.TryDequeue(out _)) { }
    }

    private void Trim()
    {
        while (_map.Count > _capacity && _order.TryDequeue(out var oldest))
            _map.TryRemove(oldest, out _);
    }
}
