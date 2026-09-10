
#nullable enable

using System.Collections.Concurrent;

namespace GraphFramework.Pipeline
{
    internal class CacheEntry
    {
        public object? Value { get; }
        public DateTime ExpiresAt { get; }

        public CacheEntry(object? value, DateTime expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    public class PipelineStepCacheConfig
    {
        public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

        public int MaxEntries { get; set; } = 1000;

        public bool Enabled { get; set; } = true;

        public string KeyPrefix { get; set; } = "";

        public static PipelineStepCacheConfig CreateDefault()
            => new();

        public static PipelineStepCacheConfig CreateDisabled()
            => new() { Enabled = false };

        public static PipelineStepCacheConfig CreateLongLived(TimeSpan? ttl = null)
            => new() { DefaultTtl = ttl ?? TimeSpan.FromHours(1) };
    }

    public class PipelineStepCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly PipelineStepCacheConfig _config;
        private readonly Timer? _cleanupTimer;
        private readonly object _evictLock = new();
        private bool _disposed;

        public long HitCount { get; private set; }

        public long MissCount { get; private set; }

        public int Count => _cache.Count;

        public PipelineStepCacheConfig Config => _config;

        public PipelineStepCache(PipelineStepCacheConfig? config = null)
        {
            _config = config ?? PipelineStepCacheConfig.CreateDefault();

            if (_config.Enabled)
            {
                _cleanupTimer = new Timer(
                    _ => CleanupExpired(),
                    null,
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(60));
            }
        }

        public string GenerateKey(string stepName, params object?[] inputs)
        {
            var keyBuilder = new System.Text.StringBuilder();
            keyBuilder.Append(_config.KeyPrefix);
            keyBuilder.Append(stepName);
            keyBuilder.Append(':');

            foreach (var input in inputs)
            {
                if (input == null)
                {
                    keyBuilder.Append("null");
                }
                else if (input is string str)
                {
                    keyBuilder.Append('s');
                    keyBuilder.Append(str.Length);
                    keyBuilder.Append(':');
                    keyBuilder.Append(str);
                }
                else if (input is int i)
                {
                    keyBuilder.Append('i');
                    keyBuilder.Append(i);
                }
                else if (input is long l)
                {
                    keyBuilder.Append('l');
                    keyBuilder.Append(l);
                }
                else if (input is double d)
                {
                    keyBuilder.Append('d');
                    keyBuilder.Append(d.ToString("G17"));
                }
                else if (input is bool b)
                {
                    keyBuilder.Append('b');
                    keyBuilder.Append(b ? '1' : '0');
                }
                else
                {
                    keyBuilder.Append('t');
                    keyBuilder.Append(input.GetType().Name);
                    keyBuilder.Append(':');
                    keyBuilder.Append(input.ToString());
                }
                keyBuilder.Append('_');
            }

            return keyBuilder.ToString();
        }

        public bool TryGetValue<T>(string key, out T? value)
        {
            value = default;

            if (!_config.Enabled)
            {
                MissCount++;
                return false;
            }

            if (_cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired)
                {
                    value = (T?)entry.Value;
                    HitCount++;
                    return true;
                }

                _cache.TryRemove(key, out _);
            }

            MissCount++;
            return false;
        }

        public void SetValue<T>(string key, T? value, TimeSpan? ttl = null)
        {
            if (!_config.Enabled)
                return;

            EvictIfNeeded();

            var expiresAt = DateTime.UtcNow.Add(ttl ?? _config.DefaultTtl);
            _cache[key] = new CacheEntry(value, expiresAt);
        }

        public T? GetOrAdd<T>(string key, Func<T?> factory, TimeSpan? ttl = null)
        {
            if (!_config.Enabled)
                return factory();

            if (_cache.TryGetValue(key, out var existing) && !existing.IsExpired)
            {
                HitCount++;
                return (T?)existing.Value;
            }

            var value = factory();
            var expiresAt = DateTime.UtcNow.Add(ttl ?? _config.DefaultTtl);
            var newEntry = new CacheEntry(value, expiresAt);

            _cache.AddOrUpdate(key, newEntry, (_, oldEntry) =>
            {
                if (oldEntry.IsExpired || ReferenceEquals(oldEntry, existing))
                    return newEntry;
                return oldEntry;
            });

            EvictIfNeeded();

            return value;
        }

        private void EvictIfNeeded()
        {
            lock (_evictLock)
            {
                if (_cache.Count < _config.MaxEntries)
                    return;

                CleanupExpired();

                if (_cache.Count >= _config.MaxEntries)
                {
                    var keyToRemove = _cache.Keys.FirstOrDefault();
                    if (keyToRemove != null)
                        _cache.TryRemove(keyToRemove, out _);
                }
            }
        }

        public bool Remove(string key)
        {
            return _cache.TryRemove(key, out _);
        }

        public int RemoveByPrefix(string prefix)
        {
            int count = 0;
            var keysToRemove = _cache.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out _))
                    count++;
            }

            return count;
        }

        public void Clear()
        {
            _cache.Clear();
            HitCount = 0;
            MissCount = 0;
        }

        public void CleanupExpired()
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.IsExpired)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }

        public double GetHitRate()
        {
            var total = HitCount + MissCount;
            return total > 0 ? (double)HitCount / total : 0;
        }

        public string GetStats()
        {
            return $"Cache: {Count} entries, {HitCount} hits, {MissCount} misses, " +
                   $"Hit rate: {GetHitRate():P1}";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _cache.Clear();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
