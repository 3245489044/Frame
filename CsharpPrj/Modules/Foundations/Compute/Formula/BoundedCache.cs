using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Modules.Compute.Formula;

/// <summary>淘汰策略（显式声明，禁各处自定义）。</summary>
public enum EvictionPolicy
{
    /// <summary>按登记序先进先出淘汰（软上限）。</summary>
    Fifo = 0,

    /// <summary>写计数达上限整表清空（保留既有 TileCache 语义）。</summary>
    ClearAll = 1,
}

/// <summary>
/// 有界缓存基元（单一实现）：容量软上限 + 显式淘汰策略；键/值泛型，桶语义由调用方决定。
/// <para>并发：<see cref="ConcurrentDictionary{TKey,TValue}"/>，容量为软上限（按登记序淘汰，不逐条精确计数）。</para>
/// <para>语义：<see cref="GetOrAdd"/> 同键首写为准（同键同引用）；<see cref="Set"/> 后写覆盖（去重时间戳等可变值）。</para>
/// </summary>
/// <typeparam name="TKey">键类型。</typeparam>
/// <typeparam name="TValue">值类型。</typeparam>
public sealed class BoundedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _entries = new();
    private readonly ConcurrentQueue<TKey> _order = new();
    private readonly int _capacity;
    private readonly EvictionPolicy _policy;
    private int _writes;

    /// <param name="capacity">容量上限（≤0 ⇒ 采用 <paramref name="fallbackCapacity"/>）。</param>
    /// <param name="fallbackCapacity">非法容量的有界缺省。</param>
    /// <param name="policy">淘汰策略（缺省 FIFO）。</param>
    public BoundedCache(int capacity, int fallbackCapacity = 256, EvictionPolicy policy = EvictionPolicy.Fifo)
    {
        _capacity = capacity > 0 ? capacity : fallbackCapacity;
        _policy = policy;
    }

    /// <summary>当前条目数（诊断用；大容量下代价高，禁在热路径逐次读取）。</summary>
    public int Count => _entries.Count;

    /// <summary>生效容量上限。</summary>
    public int Capacity => _capacity;

    /// <summary>生效淘汰策略。</summary>
    public EvictionPolicy Policy => _policy;

    /// <summary>按键取值（不改变登记序）。</summary>
    public bool TryGet(TKey key, out TValue? value) => _entries.TryGetValue(key, out value);

    /// <summary>登记（同键首写为准）；容量满按策略淘汰。</summary>
    /// <returns>是否新增（同键已存在 ⇒ false）。</returns>
    public bool TryAdd(TKey key, TValue value)
    {
        PrepareWrite();
        if (!_entries.TryAdd(key, value)) return false;
        _order.Enqueue(key);
        CompleteWrite();
        return true;
    }

    /// <summary>登记（同键首写为准）并返回驻留值。</summary>
    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (TryAdd(key, value)) return value;
        return _entries.TryGetValue(key, out var existing) ? existing : value;
    }

    /// <summary>登记/覆盖（后写为准，不改变登记序）；容量满按策略淘汰。</summary>
    public TValue Set(TKey key, TValue value)
    {
        PrepareWrite();
        if (_entries.TryAdd(key, value))
        {
            _order.Enqueue(key);
        }
        else
        {
            _entries[key] = value;
        }

        CompleteWrite();
        return value;
    }

    /// <summary>移除单键。</summary>
    public bool TryRemove(TKey key, out TValue? value) => _entries.TryRemove(key, out value);

    /// <summary>按谓词批量移除（签名失效等）；返回移除条数。</summary>
    public int RemoveWhere(Func<TKey, bool> predicate)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));

        int removed = 0;
        foreach (var key in _entries.Keys)
        {
            if (predicate(key) && _entries.TryRemove(key, out _)) removed++;
        }

        return removed;
    }

    /// <summary>按登记序淘汰至容量内（FIFO 策略；显式调用亦安全）。</summary>
    public void Trim()
    {
        while (_entries.Count > _capacity && _order.TryDequeue(out var oldest))
        {
            _entries.TryRemove(oldest, out _);
        }
    }

    /// <summary>清空（容量不变；写计数归零）。</summary>
    public void Clear()
    {
        _entries.Clear();
        while (_order.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _writes, 0);
    }

    private void PrepareWrite()
    {
        if (_policy == EvictionPolicy.ClearAll && Volatile.Read(ref _writes) >= _capacity)
        {
            Clear();
        }
    }

    private void CompleteWrite()
    {
        if (_policy == EvictionPolicy.ClearAll)
        {
            Interlocked.Increment(ref _writes);
            return;
        }

        Trim();
    }
}
