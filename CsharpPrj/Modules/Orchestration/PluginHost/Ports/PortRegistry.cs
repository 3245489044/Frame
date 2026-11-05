using System;
using System.Collections.Generic;
using System.Linq;
using PluginHostContract;

namespace PluginHost;

/// <summary>
/// <see cref="IPortRegistry"/> 的进程级唯一实现。
/// <para>
/// 纪律：① 线程安全（单锁）；② 值按 key 唯一（同一 key 换 owner ⇒ 后者覆盖并记冲突，不静默）；
/// ③ <see cref="Unbind"/> 只回收该 owner 拥有的 key（隔离的结构保证）；
/// ④ 不持有任何插件类型引用（只存 <c>object?</c>），避免跨 ALC 类型固化。
/// </para>
/// </summary>
public sealed class PortRegistry : IPortRegistry
{
    private readonly Dictionary<string, Entry> _ports = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _conflicts;

    public void Bind(string ownerId, string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            if (_ports.TryGetValue(key, out var existing)
                && !string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
            {
                _conflicts++;
            }

            _ports[key] = new Entry(ownerId, value);
        }
    }

    public bool TryGet<T>(string key, out T? value) where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (_gate)
        {
            if (!_ports.TryGetValue(key, out var entry) || entry.Value is null)
                return false;

            value = entry.Value as T;
            return value is not null;
        }
    }

    public bool Has(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (_gate)
            return _ports.TryGetValue(key, out var entry) && entry.Value is not null;
    }

    public IReadOnlyList<string> KeysOf(string ownerId)
    {
        lock (_gate)
        {
            return _ports
                .Where(kv => string.Equals(kv.Value.OwnerId, ownerId, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public int Unbind(string ownerId)
    {
        lock (_gate)
        {
            var keys = _ports
                .Where(kv => string.Equals(kv.Value.OwnerId, ownerId, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToArray();

            foreach (var key in keys)
                _ports.Remove(key);

            return keys.Length;
        }
    }

    public IReadOnlyDictionary<string, string> OwnerSnapshot
    {
        get
        {
            lock (_gate)
                return _ports.ToDictionary(kv => kv.Key, kv => kv.Value.OwnerId, StringComparer.Ordinal);
        }
    }

    /// <summary>跨 owner 覆盖冲突计数（可见化：非零即存在双写者信号）。</summary>
    public int ConflictCount
    {
        get { lock (_gate) return _conflicts; }
    }

    private readonly record struct Entry(string OwnerId, object? Value);
}
