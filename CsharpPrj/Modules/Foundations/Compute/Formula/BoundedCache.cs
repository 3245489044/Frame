using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Modules.Compute.Formula;

/// <summary>
/// 有界缓存基元（单一实现）：容量软上限 + 先进先出淘汰；键/值泛型，桶语义由调用方决定。
/// <para>并发：<see cref="ConcurrentDictionary{TKey,TValue}"/>，容量为软上限（按登记序淘汰，不逐条精确计数）。</para>
/// <para>语义：同键首写为准（后续写入不覆盖，返回既有值），保证"同键同引用"。</para>
/// </summary>
/// <typeparam name="TKey">键类型。</typeparam>
/// <typeparam name="TValue">值类型。</typeparam>
public sealed class BoundedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _entries = new();
    private readonly ConcurrentQueue<TKey> _order = new();
    private readonly int _capacity;

    /// <param name="capacity">容量上限（≤0 ⇒ 采用 <paramref name="fallbackCapacity"/>）。</param>
    /// <param name="fallbackCapacity">非法容量的有界缺省。</param>
    public BoundedCache(int capacity, int fallbackCapacity = 256)
        => _capacity = capacity > 0 ? capacity : fallbackCapacity;

    /// <summary>当前条目数（诊断用；大容量下代价高，禁在热路径逐次读取）。</summary>
    public int Count => _entries.Count;

    /// <summary>生效容量上限。</summary>
    public int Capacity => _capacity;

    /// <summary>按键取值（不改变登记序）。</summary>
    public bool TryGet(TKey key, out TValue? value) => _entries.TryGetValue(key, out value);

    /// <summary>登记（同键首写为准）并返回驻留值。</summary>
    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (_entries.TryAdd(key, value))
        {
            _order.Enqueue(key);
            Trim();
            return value;
        }

        return _entries.TryGetValue(key, out var existing) ? existing : value;
    }

    /// <summary>移除单键。</summary>
    public bool TryRemove(TKey key, out TValue? value) => _entries.TryRemove(key, out value);

    /// <summary>按登记序淘汰至容量内。</summary>
    public void Trim()
    {
        while (_entries.Count > _capacity && _order.TryDequeue(out var oldest))
        {
            _entries.TryRemove(oldest, out _);
        }
    }

    /// <summary>清空（容量不变）。</summary>
    public void Clear()
    {
        _entries.Clear();
        while (_order.TryDequeue(out _))
        {
        }
    }
}
