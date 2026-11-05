#nullable enable

using System;
using System.Collections.Generic;

namespace Share.StateManagement;

/// <summary>
/// 有界重放缓存（通用件，线程安全）：键 → 判定；超容按最久未用淘汰（LRU），非整体清空。
/// </summary>
/// <typeparam name="TValue">判定值类型（如 decision 词）。</typeparam>
public sealed class BoundedReplayCache<TValue> where TValue : notnull
{
    private readonly Dictionary<string, TValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly object _gate = new();
    private readonly int _capacity;

    public BoundedReplayCache(int capacity = 4096)
        => _capacity = capacity < 1 ? 1 : capacity;

    /// <summary>当前条目数。</summary>
    public int Count
    {
        get { lock (_gate) return _values.Count; }
    }

    /// <summary>容量（<see cref="Count"/> 超此值即淘汰最旧）。</summary>
    public int Capacity => _capacity;

    /// <summary>命中即刷新为最近使用；未命中返回 false。</summary>
    public bool TryGet(string? key, out TValue value)
    {
        value = default!;
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out value!)) return false;
            Touch(key);
            return true;
        }
    }

    /// <summary>写入/覆盖判定并刷新为最近使用（超容淘汰最旧）。</summary>
    public void Remember(string key, TValue value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("重放键不能为空。", nameof(key));
        lock (_gate)
        {
            _values[key] = value;
            Touch(key);
            while (_values.Count > _capacity && _order.First is { } oldest)
            {
                _order.RemoveFirst();
                _values.Remove(oldest.Value);
                _nodes.Remove(oldest.Value);
            }
        }
    }

    /// <summary>清空（测试/换代语义）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _values.Clear();
            _nodes.Clear();
            _order.Clear();
        }
    }

    private void Touch(string key)
    {
        if (_nodes.TryGetValue(key, out var node)) _order.Remove(node);
        _nodes[key] = _order.AddLast(key);
    }
}
