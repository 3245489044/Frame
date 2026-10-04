
#nullable enable
using System.Collections.Concurrent;
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Core
{
    public class PoolContext : IPoolContext
    {
        protected readonly ConcurrentDictionary<string, object?> _store;

        public string ContextId { get; }

        public PoolContext(string? contextId = null, IEqualityComparer<string>? comparer = null)
        {
            ContextId = contextId ?? Guid.NewGuid().ToString("N");
            _store = new ConcurrentDictionary<string, object?>(comparer ?? StringComparer.OrdinalIgnoreCase);
        }

        public object? Get(string key)
        {
            PortGuard.Required(key, nameof(key));
            return _store.TryGetValue(key, out var value) ? value : null;
        }

        public void Set(string key, object? value)
        {
            PortGuard.Required(key, nameof(key));
            _store[key] = value;
        }

        public T? Get<T>(string key)
        {
            PortGuard.Required(key, nameof(key));
            if (_store.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return default;
        }

        public bool Contains(string key)
        {
            PortGuard.Required(key, nameof(key));
            return _store.ContainsKey(key);
        }

        public IEnumerable<string> Keys => _store.Keys;

        public IPoolContext Clone()
        {
            var clone = new PoolContext();
            foreach (var kvp in _store)
                clone.Set(kvp.Key, kvp.Value);
            return clone;
        }

        public override string ToString()
            => $"PoolContext[{ContextId}] Count={_store.Count}";
    }
}
