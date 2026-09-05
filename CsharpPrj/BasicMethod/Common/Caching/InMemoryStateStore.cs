
using System.Collections.Concurrent;

namespace Modules.Common.Caching
{
    public class InMemoryStateStore : IStateStore
    {
        private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string key, string? value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            _values[key] = value;
        }

        public string? Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _values.GetValueOrDefault(key);
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return _values.ContainsKey(key);
        }

        public void Clear() => _values.Clear();

        public IReadOnlyCollection<string> GetAllKeys() => _values.Keys.ToList().AsReadOnly();

        public List<string> GetAllKeysList() => _values.Keys.ToList();

        public int Count => _values.Count;
    }
}
