
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BasicMethod.SortMent
{

    public class SortMentRegistry<TKey, TSource, TTarget> : ISortMentRegistry<TKey, TSource, TTarget>
        where TKey : notnull
    {
        protected readonly ConcurrentDictionary<TKey, ISortMent<TSource, TTarget>> _items = new();

        public int Count => _items.Count;

        public IEnumerable<TKey> Keys => _items.Keys;

        public void Register(TKey key, ISortMent<TSource, TTarget> sortMent)
        {
            if (!_items.TryAdd(key, sortMent))
                throw new ArgumentException($"键 '{key}' 已注册", nameof(key));
        }

        public void RegisterOrUpdate(TKey key, ISortMent<TSource, TTarget> sortMent)
        {
            _items.AddOrUpdate(key, sortMent, (_, _) => sortMent);
        }

        public ISortMent<TSource, TTarget> Get(TKey key)
        {
            if (_items.TryGetValue(key, out var sortMent))
                return sortMent;
            throw new KeyNotFoundException($"键 '{key}' 未注册");
        }

        public bool TryGet(TKey key, out ISortMent<TSource, TTarget>? sortMent)
        {
            return _items.TryGetValue(key, out sortMent);
        }

        public bool ContainsKey(TKey key) => _items.ContainsKey(key);

        public bool Remove(TKey key) => _items.TryRemove(key, out _);

        public void Clear() => _items.Clear();

        public TTarget Execute(TKey key, TSource source)
        {
            var sortMent = Get(key);
            return sortMent.Morph(source);
        }
    }
}
