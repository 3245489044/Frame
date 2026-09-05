
using System.Collections.Concurrent;

namespace Share.StateManagement
{
    public interface ITtlCache<TKey, TValue>
        where TKey : notnull
    {
        void Set(TKey key, TValue value);

        void Set(TKey key, TValue value, TimeSpan ttl);

        bool TryGet(TKey key, out TValue? value);

        TValue? Get(TKey key);

        bool Remove(TKey key);

        bool Contains(TKey key);

        void Clear();

        int ClearExpired();

        int Count { get; }

        int ActiveCount { get; }

        IEnumerable<TKey> GetAllKeys();

        CacheStatistics GetStatistics();
    }

    public class CacheStatistics
    {
        public int TotalCount { get; set; }

        public int ActiveCount { get; set; }

        public int ExpiredCount { get; set; }

        public TimeSpan DefaultTtl { get; set; }

        public long HitCount { get; set; }

        public long MissCount { get; set; }

        public double HitRate => (HitCount + MissCount) > 0
            ? (double)HitCount / (HitCount + MissCount) * 100
            : 0;
    }

    public class TtlCache<TKey, TValue> : ITtlCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
        private readonly TimeSpan _defaultTtl;
        private long _hitCount;
        private long _missCount;

        public TtlCache(TimeSpan? defaultTtl = null, IEqualityComparer<TKey>? comparer = null)
        {
            _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(30);
            _cache = new ConcurrentDictionary<TKey, CacheEntry>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public static TtlCache<string, TValue> CreateOrdinalIgnoreCase(TimeSpan? defaultTtl = null)
            => new TtlCache<string, TValue>(defaultTtl, StringComparer.OrdinalIgnoreCase);

        public void Set(TKey key, TValue value)
        {
            Set(key, value, _defaultTtl);
        }

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);

            _cache[key] = new CacheEntry
            {
                Data = value,
                CachedAt = DateTime.UtcNow,
                Ttl = ttl
            };
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAt < entry.Ttl)
                {
                    value = entry.Data;
                    Interlocked.Increment(ref _hitCount);
                    return true;
                }

                _cache.TryRemove(key, out _);
            }

            value = default;
            Interlocked.Increment(ref _missCount);
            return false;
        }

        public TValue? Get(TKey key)
        {
            return TryGet(key, out var value) ? value : default;
        }

        public bool Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _cache.TryRemove(key, out _);
        }

        public bool Contains(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAt < entry.Ttl)
                    return true;

                _cache.TryRemove(key, out _);
            }
            return false;
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public int ClearExpired()
        {
            var now = DateTime.UtcNow;
            var expired = _cache.Where(kvp => now - kvp.Value.CachedAt >= kvp.Value.Ttl).ToList();

            foreach (var kvp in expired)
            {
                _cache.TryRemove(kvp.Key, out _);
            }

            return expired.Count;
        }

        public int Count => _cache.Count;

        public int ActiveCount
        {
            get
            {
                var now = DateTime.UtcNow;
                return _cache.Values.Count(e => now - e.CachedAt < e.Ttl);
            }
        }

        public IEnumerable<TKey> GetAllKeys() => _cache.Keys.ToList();

        public CacheStatistics GetStatistics()
        {
            var now = DateTime.UtcNow;
            int total = _cache.Count;
            int expired = _cache.Values.Count(e => now - e.CachedAt >= e.Ttl);

            return new CacheStatistics
            {
                TotalCount = total,
                ActiveCount = total - expired,
                ExpiredCount = expired,
                DefaultTtl = _defaultTtl,
                HitCount = Interlocked.Read(ref _hitCount),
                MissCount = Interlocked.Read(ref _missCount)
            };
        }

        private class CacheEntry
        {
            public TValue Data { get; set; } = default!;
            public DateTime CachedAt { get; set; }
            public TimeSpan Ttl { get; set; }
        }
    }
}
