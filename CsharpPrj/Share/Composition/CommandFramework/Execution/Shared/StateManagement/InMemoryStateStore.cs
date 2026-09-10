
using System.Collections.Concurrent;
using CommandFramework.Abstractions;

namespace CommandFramework.Execution
{
    public class InMemoryStateStore : IStateStore
    {
        private readonly ConcurrentDictionary<string, string?> _store = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            _store[key] = value;
        }

        public string? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return _store.TryGetValue(key, out var value) ? value : null;
        }

        public bool Has(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return _store.ContainsKey(key);
        }

        public string? GetOrAdd(string key, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            return _store.GetOrAdd(key, defaultValue);
        }

        public void Clear()
        {
            _store.Clear();
        }

        public IReadOnlyCollection<string> GetAllKeys()
        {
            return _store.Keys.ToArray();
        }

        public int Count => _store.Count;

        public void Remove(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _store.TryRemove(key, out _);
        }

        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            foreach (var key in GetAllKeys())
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    Remove(key);
                }
            }
        }
    }
}
