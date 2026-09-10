
namespace Share.StateManagement
{
    public class InMemoryStateStore : IStateStore
    {
        private readonly StringKvStoreBacking _backing = new();

        public void Set(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            _backing.Set(key, value);
        }

        public string? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return _backing.Get(key);
        }

        public bool Has(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return _backing.Has(key);
        }

        public void Clear() => _backing.Clear();

        public IReadOnlyCollection<string> GetAllKeys() => _backing.Keys;

        public string? GetOrAdd(string key, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            return _backing.GetOrAdd(key, defaultValue);
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _backing.Remove(key);
        }

        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;

            _backing.RemoveByPrefix(prefix);
        }

        public void SetRange(IEnumerable<KeyValuePair<string, string?>> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            foreach (var kvp in values)
            {
                Set(kvp.Key, kvp.Value);
            }
        }

        public Dictionary<string, string?> GetAll() => _backing.Snapshot();

        public int Count => _backing.Count;

        public bool IsEmpty => _backing.Count == 0;
    }
}
