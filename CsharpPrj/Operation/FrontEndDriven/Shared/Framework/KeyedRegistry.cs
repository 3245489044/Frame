#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 键值注册表（通用框架）：以并发字典为后端的线程安全键-值存储，覆盖
/// SplitterPanelRegistry / AppliedSnapshotRegistry 等"注册/解析/注销/清理"同构注册表。
/// </summary>
/// <typeparam name="TKey">键类型。</typeparam>
/// <typeparam name="TValue">值类型。</typeparam>
public class KeyedRegistry<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _items;

    public KeyedRegistry(IEqualityComparer<TKey>? comparer = null)
    {
        _items = comparer == null
            ? new ConcurrentDictionary<TKey, TValue>()
            : new ConcurrentDictionary<TKey, TValue>(comparer);
    }

    public int Count => _items.Count;

    public ICollection<TValue> Values => _items.Values;

    public ICollection<TKey> Keys => _items.Keys;

    /// <summary>注册/覆盖键值。</summary>
    public void Register(TKey key, TValue value) => _items[key] = value;

    public bool TryGet(TKey key, out TValue value) => _items.TryGetValue(key, out value!);

    public TValue? Get(TKey key) => _items.TryGetValue(key, out var value) ? value : default;

    public bool Contains(TKey key) => _items.ContainsKey(key);

    public bool Remove(TKey key) => _items.TryRemove(key, out _);

    public void Clear() => _items.Clear();
}
