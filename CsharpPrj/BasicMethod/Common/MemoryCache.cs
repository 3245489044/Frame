#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Modules.Common
{
    public class MemoryCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, CacheItem> _cache = new();
        private readonly int? _capacity;
        private readonly LinkedList<TKey> _accessOrder = new();
        private readonly object _lruLock = new();

        private long _hitCount;
        private long _missCount;

        public MemoryCache(int? capacity = null)
        {
            _capacity = capacity;
        }

        public long HitCount => Interlocked.Read(ref _hitCount);
        public long MissCount => Interlocked.Read(ref _missCount);
        public double HitRate
        {
            get
            {
                long hits = HitCount, misses = MissCount;
                long total = hits + misses;
                return total > 0 ? (double)hits / total : 0.0;
            }
        }

        private class CacheItem
        {
            public TValue Value { get; }
            public DateTime? Expiration { get; }
            public DateTime CreatedAt { get; } = DateTime.UtcNow;

            public CacheItem(TValue value, DateTime? expiration)
            {
                Value = value;
                Expiration = expiration;
            }

            public bool IsExpired => Expiration.HasValue && DateTime.UtcNow >= Expiration.Value;
        }

        private void RecordAccess(TKey key)
        {
            if (_capacity.HasValue)
            {
                lock (_lruLock)
                {
                    _accessOrder.Remove(key);
                    _accessOrder.AddLast(key);
                }
            }
        }

        private void EvictIfNeeded()
        {
            if (!_capacity.HasValue) return;
            lock (_lruLock)
            {
                while (_cache.Count > _capacity.Value && _accessOrder.First != null)
                {
                    var key = _accessOrder.First.Value;
                    _accessOrder.RemoveFirst();
                    if (_cache.TryRemove(key, out _))
                    {
                        _evictedCount++;
                    }
                }
            }
        }

        private long _evictedCount;
        public long EvictedCount => Interlocked.Read(ref _evictedCount);

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
            => GetOrAdd(key, factory, null);

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory, TimeSpan? expiration)
        {
            DateTime? expiryTime = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null;
            const int maxRetries = 3;
            int retryCount = 0;

            while (true)
            {
                if (retryCount >= maxRetries)
                    throw new InvalidOperationException(
                        $"缓存操作重试超过 {maxRetries} 次，键 '{key}' 在并发访问下无法完成操作。");

                if (_cache.TryGetValue(key, out var existing))
                {
                    if (!existing.IsExpired)
                    {
                        Interlocked.Increment(ref _hitCount);
                        RecordAccess(key);
                        return existing.Value;
                    }

                    var newItem = new CacheItem(factory(key), expiryTime);
                    if (_cache.TryUpdate(key, newItem, existing))
                    {
                        Interlocked.Increment(ref _missCount);
                        RecordAccess(key);
                        return newItem.Value;
                    }
                    retryCount++;
                    continue;
                }

                var added = _cache.GetOrAdd(key, _ => new CacheItem(factory(key), expiryTime));
                if (!added.IsExpired)
                {
                    Interlocked.Increment(ref _hitCount);
                    RecordAccess(key);
                    EvictIfNeeded();
                    return added.Value;
                }

                _cache.TryRemove(key, out _);
                Interlocked.Increment(ref _missCount);
                retryCount++;
            }
        }

        public TValue GetOrAdd(TKey key, TValue value)
            => GetOrAdd(key, _ => value);

        public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (_cache.TryGetValue(key, out var item) && !item.IsExpired)
            {
                value = item.Value;
                Interlocked.Increment(ref _hitCount);
                RecordAccess(key);
                return true;
            }
            value = default;
            Interlocked.Increment(ref _missCount);
            return false;
        }

        public void Remove(TKey key)
        {
            _cache.TryRemove(key, out _);
            if (_capacity.HasValue)
            {
                lock (_lruLock)
                {
                    _accessOrder.Remove(key);
                }
            }
        }

        public void Clear()
        {
            _cache.Clear();
            if (_capacity.HasValue)
            {
                lock (_lruLock)
                {
                    _accessOrder.Clear();
                }
            }
        }

        public int Count => _cache.Count;

        public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                if (!Equals(existing.Value, comparisonValue))
                    return false;
                var newItem = new CacheItem(newValue, existing.Expiration);
                return _cache.TryUpdate(key, newItem, existing);
            }
            return false;
        }

        public IEnumerable<TKey> Keys => _cache.Keys;

        public void CleanupExpired()
        {
            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                    _cache.TryRemove(kvp.Key, out _);
            }
        }
    }
}
