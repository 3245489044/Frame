
#nullable enable
using System.Collections.Concurrent;
using Share.TaskStatusPool.Abstractions;
using Share.TaskStatusPool.Models;

namespace Share.TaskStatusPool.Core
{
    public class ReadWritePool<TKey, TValue> : IReadWritePool<TKey, TValue>
        where TKey : notnull
    {
        protected readonly ConcurrentDictionary<TKey, TValue?> _store;

        public string Name { get; }

        public int Count => _store.Count;

        public bool IsEmpty => _store.IsEmpty;

        public event EventHandler<PoolChangedEventArgs<TValue>>? Changed;

        public ReadWritePool(string? name = null, IEqualityComparer<TKey>? comparer = null)
        {
            Name = name ?? $"Pool<{typeof(TKey).Name},{typeof(TValue).Name}>";
            _store = new ConcurrentDictionary<TKey, TValue?>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public TValue? Read(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryGetValue(key, out var value) ? value : default;
        }

        public bool TryRead(TKey key, out TValue? value)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryGetValue(key, out value);
        }

        public bool Contains(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.ContainsKey(key);
        }

        public IEnumerable<TKey> Keys => _store.Keys;

        public void Write(TKey key, TValue? value)
        {
            ArgumentNullException.ThrowIfNull(key);

            var oldValue = default(TValue);
            var isNew = false;

            _store.AddOrUpdate(
                key,
                (_) =>
                {
                    isNew = true;
                    return value;
                },
                (_, existing) =>
                {
                    oldValue = existing;
                    return value;
                });

            if (isNew)
                OnChanged(new PoolChangedEventArgs<TValue>(PoolChangeType.Added, newValue: value));
            else
                OnChanged(new PoolChangedEventArgs<TValue>(PoolChangeType.Updated, oldValue: oldValue, newValue: value));
        }

        public void WriteRange(IEnumerable<KeyValuePair<TKey, TValue?>> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            foreach (var kvp in items)
            {
                Write(kvp.Key, kvp.Value);
            }
        }

        public bool Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (_store.TryRemove(key, out var oldValue))
            {
                OnChanged(new PoolChangedEventArgs<TValue>(PoolChangeType.Removed, oldValue: oldValue));
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _store.Clear();
            OnChanged(new PoolChangedEventArgs<TValue>(PoolChangeType.Cleared));
        }

        protected virtual void OnChanged(PoolChangedEventArgs<TValue> args)
        {
            Changed?.Invoke(this, args);
        }

        public override string ToString()
            => $"ReadWritePool[{Name}] Count={_store.Count}";
    }
}
