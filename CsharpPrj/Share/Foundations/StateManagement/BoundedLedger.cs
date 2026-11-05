#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Share.StateManagement;

/// <summary>
/// 有界键控派生账端口（通用件）：进程内观测缓存，不持久化、不参与判定。
/// </summary>
public interface IBoundedLedger<T> where T : class
{
    /// <summary>容量上限（溢出按写入序淘汰，受保护项优先保留）。</summary>
    int Capacity { get; }

    /// <summary>当前条数。</summary>
    int Count { get; }

    /// <summary>按键读取（空白键 ⇒ null）。</summary>
    T? Get(string? key);

    /// <summary>写入（键已存在 ⇒ 不覆盖，返回 false）。</summary>
    bool TryAdd(string key, T value, Func<T, bool>? protect = null);

    /// <summary>覆盖写（键不存在则新增）。</summary>
    void Set(string key, T value, Func<T, bool>? protect = null);

    /// <summary>全部值（顺序未定义）。</summary>
    IReadOnlyList<T> Values();

    /// <summary>清空（测试/重启语义）。</summary>
    void Clear();
}

/// <summary>
/// 有界键控派生账（唯一实现）：溢出按写入序淘汰；<c>protect</c> 为真的项优先保留。
/// </summary>
public class BoundedLedger<T> : IBoundedLedger<T> where T : class
{
    private readonly ConcurrentDictionary<string, T> _map = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    public BoundedLedger(int capacity = 512) => Capacity = capacity <= 0 ? 512 : capacity;

    /// <inheritdoc />
    public int Capacity { get; }

    /// <inheritdoc />
    public int Count => _map.Count;

    /// <inheritdoc />
    public T? Get(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : _map.TryGetValue(key.Trim(), out var value) ? value : null;

    /// <inheritdoc />
    public bool TryAdd(string key, T value, Func<T, bool>? protect = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = key.Trim();
        if (!_map.TryAdd(normalized, value)) return false;
        _order.Enqueue(normalized);
        Trim(protect);
        return true;
    }

    /// <inheritdoc />
    public void Set(string key, T value, Func<T, bool>? protect = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var normalized = key.Trim();
        var added = !_map.ContainsKey(normalized);
        _map[normalized] = value;
        if (added) _order.Enqueue(normalized);
        Trim(protect);
    }

    /// <inheritdoc />
    public IReadOnlyList<T> Values() => _map.Values.ToList();

    /// <inheritdoc />
    public void Clear()
    {
        _map.Clear();
        while (_order.TryDequeue(out _)) { }
    }

    private void Trim(Func<T, bool>? protect)
    {
        var attempts = 0;
        var max = _order.Count;
        while (_map.Count > Capacity && attempts++ < max && _order.TryDequeue(out var stale))
        {
            if (_map.TryGetValue(stale, out var value) && protect is not null && protect(value))
            {
                _order.Enqueue(stale);
                continue;
            }

            _map.TryRemove(stale, out _);
        }
    }
}
