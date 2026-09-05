
using System.Collections.Concurrent;

namespace Share.StateManagement
{
    public interface IKeyValueStore<TKey, TValue>
        where TKey : notnull
    {
        void Store(TKey key, TValue value);

        TValue? Retrieve(TKey key);

        bool TryRetrieve(TKey key, out TValue? value);

        bool Remove(TKey key);

        bool Contains(TKey key);

        IEnumerable<TKey> GetAllKeys();

        IEnumerable<TValue> GetAllValues();

        int Count { get; }

        void Clear();

        void StoreRange(IEnumerable<KeyValuePair<TKey, TValue>> items);

        TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);

        event EventHandler<StoreChangedEventArgs<TKey, TValue>>? Changed;
    }

    public class StoreChangedEventArgs<TKey, TValue> : EventArgs
    {
        public StoreChangeType ChangeType { get; }

        public TKey Key { get; }

        public TValue? OldValue { get; }

        public TValue? NewValue { get; }

        public StoreChangedEventArgs(StoreChangeType changeType, TKey key, TValue? oldValue = default, TValue? newValue = default)
        {
            ChangeType = changeType;
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    public enum StoreChangeType
    {
        Added,
        Updated,
        Removed,
        Cleared
    }

    public class InMemoryStore<TKey, TValue> : IKeyValueStore<TKey, TValue>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TValue> _store;

        public event EventHandler<StoreChangedEventArgs<TKey, TValue>>? Changed;

        public InMemoryStore(IEqualityComparer<TKey>? comparer = null)
        {
            _store = new ConcurrentDictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public static InMemoryStore<string, TValue> CreateOrdinalIgnoreCase()
            => new InMemoryStore<string, TValue>(StringComparer.OrdinalIgnoreCase);

        public void Store(TKey key, TValue value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);

            bool isUpdate = _store.ContainsKey(key);
            TValue? oldValue = isUpdate ? _store[key] : default;

            _store[key] = value;

            OnChanged(new StoreChangedEventArgs<TKey, TValue>(
                isUpdate ? StoreChangeType.Updated : StoreChangeType.Added,
                key, oldValue, value));
        }

        public TValue? Retrieve(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryGetValue(key, out var value) ? value : default;
        }

        public bool TryRetrieve(TKey key, out TValue? value)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryGetValue(key, out value);
        }

        public bool Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (_store.TryRemove(key, out var oldValue))
            {
                OnChanged(new StoreChangedEventArgs<TKey, TValue>(StoreChangeType.Removed, key, oldValue, default));
                return true;
            }
            return false;
        }

        public bool Contains(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.ContainsKey(key);
        }

        public IEnumerable<TKey> GetAllKeys() => _store.Keys.ToList();

        public IEnumerable<TValue> GetAllValues() => _store.Values.ToList();

        public int Count => _store.Count;

        public void Clear()
        {
            _store.Clear();
            OnChanged(new StoreChangedEventArgs<TKey, TValue>(StoreChangeType.Cleared, default!));
        }

        public void StoreRange(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            foreach (var kvp in items)
            {
                Store(kvp.Key, kvp.Value);
            }
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(valueFactory);

            return _store.GetOrAdd(key, k =>
            {
                var value = valueFactory(k);
                OnChanged(new StoreChangedEventArgs<TKey, TValue>(StoreChangeType.Added, k, default, value));
                return value;
            });
        }

        protected virtual void OnChanged(StoreChangedEventArgs<TKey, TValue> e)
        {
            Changed?.Invoke(this, e);
        }
    }
}
