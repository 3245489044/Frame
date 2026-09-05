#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
#endregion

namespace MathematicalAlgorithms
{
    internal class ClusteringCacheEntry
    {
        public object Value { get; }
        public DateTime ExpirationTime { get; }
        public DateTime LastAccessed { get; set; }
        public long Size { get; }

        public ClusteringCacheEntry(object value, DateTime expirationTime, long size)
        {
            Value = value;
            ExpirationTime = expirationTime;
            LastAccessed = DateTime.UtcNow;
            Size = size;
        }

        public bool IsExpired => DateTime.UtcNow > ExpirationTime;
    }

    public class ClusteringCacheManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, ClusteringCacheEntry> _cache = new();
        private readonly long _maxSizeBytes;
        private long _currentSizeBytes;
        private readonly object _sizeLock = new();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public ClusteringCacheManager(long cacheSizeBytes = 50 * 1024 * 1024)
        {
            _maxSizeBytes = cacheSizeBytes;
            _cleanupTimer = new Timer(_ => SafeCleanupExpiredEntries(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public bool TryGet<T>(string key, out T result)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                entry.LastAccessed = DateTime.UtcNow;
                result = (T)entry.Value;
                return true;
            }
            result = default;
            return false;
        }

        public void Set(string key, object value, TimeSpan expiration)
        {
            var entrySize = EstimateSize(value);

            lock (_sizeLock)
            {
                if (_currentSizeBytes + entrySize > _maxSizeBytes)
                {
                    CleanupCache();
                }

                if (_cache.TryRemove(key, out var oldEntry))
                {
                    _currentSizeBytes -= oldEntry.Size;
                }

                var newEntry = new ClusteringCacheEntry(value, DateTime.UtcNow + expiration, entrySize);
                _cache[key] = newEntry;
                _currentSizeBytes += entrySize;
            }
        }

        public static string GenerateKey(string prefix, params object[] parameters)
        {
            var hash = string.Join("_", parameters.Select(p => p?.ToString() ?? "null"));
            var hashCode = hash.GetHashCode(StringComparison.Ordinal);
            return $"Clustering_{prefix}_{hashCode:X8}";
        }

        public void Clear()
        {
            lock (_sizeLock)
            {
                _cache.Clear();
                _currentSizeBytes = 0;
            }
        }

        public long CurrentSizeBytes => _currentSizeBytes;

        public int Count => _cache.Count;

        private void CleanupCache()
        {
            var expiredKeys = _cache.Where(kv => kv.Value.IsExpired)
                                    .Select(kv => kv.Key)
                                    .ToList();

            foreach (var key in expiredKeys)
            {
                if (_cache.TryRemove(key, out var entry))
                {
                    _currentSizeBytes -= entry.Size;
                }
            }

            if (_currentSizeBytes > _maxSizeBytes)
            {
                var oldEntries = _cache.OrderBy(kv => kv.Value.LastAccessed)
                                       .Take(Math.Max(1, _cache.Count / 5))
                                       .ToList();

                foreach (var kv in oldEntries)
                {
                    if (_cache.TryRemove(kv.Key, out var entry))
                    {
                        _currentSizeBytes -= entry.Size;
                    }
                }
            }
        }

        private void CleanupExpiredEntries()
        {
            lock (_sizeLock)
            {
                var expiredKeys = _cache.Where(kv => kv.Value.IsExpired)
                                        .Select(kv => kv.Key)
                                        .ToList();

                foreach (var key in expiredKeys)
                {
                    if (_cache.TryRemove(key, out var entry))
                    {
                        _currentSizeBytes -= entry.Size;
                    }
                }
            }
        }

        private void SafeCleanupExpiredEntries()
        {
            try
            {
                CleanupExpiredEntries();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClusteringCache] Cleanup error: {ex.Message}");
            }
        }

        private static long EstimateSize(object value)
        {
            if (value is double[,] matrix)
                return matrix.GetLength(0) * matrix.GetLength(1) * sizeof(double) + 1024;
            if (value is double[][] jagged)
                return jagged.Length * (jagged.Length > 0 ? jagged[0].Length * sizeof(double) : 0) + 1024;
            return 1024;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _cache.Clear();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
