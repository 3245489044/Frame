using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Share.HostAssembly.Storage;

/// <summary>
/// 内存键值存储（按 key 快照读写）—— 线程安全的进程内只增快照容器，
/// 供「写侧捕获快照、多读方读同一份」的场景（如提示词快照 / 运行态缓存）复用。
/// 空 key 或空值写入被忽略（幂等）。
/// </summary>
/// <typeparam name="TValue">存储值类型（引用类型）。</typeparam>
public sealed class InMemoryKeyedStore<TValue> where TValue : class
{
    private readonly ConcurrentDictionary<string, TValue> _map;

    public InMemoryKeyedStore(IEqualityComparer<string>? comparer = null)
        => _map = new ConcurrentDictionary<string, TValue>(comparer ?? StringComparer.Ordinal);

    /// <summary>读取指定 key 的快照；不存在或空 key 返回 null。</summary>
    public TValue? Read(string key)
        => string.IsNullOrEmpty(key) || !_map.TryGetValue(key, out var value) ? null : value;

    /// <summary>写入快照（覆盖式）；空 key 或空值忽略。返回是否实际写入。</summary>
    public bool TryWrite(string key, TValue value)
    {
        if (string.IsNullOrEmpty(key) || value is null) return false;
        _map[key] = value;
        return true;
    }

    /// <summary>移除指定 key；返回是否已存在。</summary>
    public bool Remove(string key)
        => !string.IsNullOrEmpty(key) && _map.TryRemove(key, out _);

    /// <summary>清空全部快照。</summary>
    public void Clear() => _map.Clear();

    /// <summary>当前快照数量。</summary>
    public int Count => _map.Count;
}
