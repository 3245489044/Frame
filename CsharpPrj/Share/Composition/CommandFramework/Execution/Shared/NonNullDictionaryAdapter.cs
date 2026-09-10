
using System.Collections;

namespace CommandFramework.Abstractions
{

    internal class NonNullDictionaryAdapter : IDictionary<string, object>
    {
        private readonly IDictionary<string, object?> _inner;

        public NonNullDictionaryAdapter(IDictionary<string, object?> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }


        public object this[string key]
        {
            get => _inner[key] ?? string.Empty;
            set => _inner[key] = value;
        }

        public ICollection<string> Keys => _inner.Keys;
        public ICollection<object> Values => _inner.Values.Select(v => v ?? (object)string.Empty).ToList();
        public int Count => _inner.Count;
        public bool IsReadOnly => _inner.IsReadOnly;

        public void Add(string key, object value) => _inner.Add(key, value);
        public void Add(KeyValuePair<string, object> item) => _inner.Add(new KeyValuePair<string, object?>(item.Key, item.Value));
        public void Clear() => _inner.Clear();
        public bool Contains(KeyValuePair<string, object> item) => _inner.Contains(new KeyValuePair<string, object?>(item.Key, item.Value));
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            var pairs = _inner.Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value ?? string.Empty)).ToArray();
            pairs.CopyTo(array, arrayIndex);
        }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var kvp in _inner)
                yield return new KeyValuePair<string, object>(kvp.Key, kvp.Value ?? string.Empty);
        }
        public bool Remove(string key) => _inner.Remove(key);
        public bool Remove(KeyValuePair<string, object> item) => _inner.Remove(new KeyValuePair<string, object?>(item.Key, item.Value));
        public bool TryGetValue(string key, out object value)
        {
            if (_inner.TryGetValue(key, out var innerVal))
            {
                value = innerVal ?? string.Empty;
                return true;
            }
            value = string.Empty;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
