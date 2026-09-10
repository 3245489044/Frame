using System.Collections.Concurrent;

namespace Share.StateManagement
{
    /// <summary>
    /// 字符串键值存储的共享后端内核（ConcurrentDictionary 实现，OrdinalIgnoreCase 键比较）。
    /// <see cref="InMemoryStateStore"/>（IStateStore）与 <see cref="VirtualStore"/>（IVirtualStore）
    /// 共用本内核，消除两套几乎逐行相同的 KV 逻辑（D1）。公共 API 由各自门面类承载，行为不变。
    /// </summary>
    internal sealed class StringKvStoreBacking
    {
        private readonly ConcurrentDictionary<string, string?> _store = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string key, string? value) => _store[key] = value;

        public string? Get(string key)
            => _store.TryGetValue(key, out var value) ? value : null;

        public bool Has(string key) => _store.ContainsKey(key);

        public bool Remove(string key) => _store.TryRemove(key, out _);

        public void Clear() => _store.Clear();

        public string? GetOrAdd(string key, string? defaultValue)
            => _store.GetOrAdd(key, defaultValue);

        public IReadOnlyCollection<string> Keys => _store.Keys.ToArray();

        public Dictionary<string, string?> Snapshot()
            => new(_store, StringComparer.OrdinalIgnoreCase);

        public int Count => _store.Count;

        public void RemoveByPrefix(string prefix)
        {
            foreach (var key in _store.Keys
                         .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _store.TryRemove(key, out _);
            }
        }
    }
}