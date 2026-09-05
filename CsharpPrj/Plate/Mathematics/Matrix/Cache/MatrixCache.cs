#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    internal class CacheSizeTracker
    {
        private long _currentSizeBytes;
        private readonly object _lock = new();

        public long CurrentSize => Interlocked.Read(ref _currentSizeBytes);

        public void Add(long size)
        {
            Interlocked.Add(ref _currentSizeBytes, size);
        }

        public void Subtract(long size)
        {
            Interlocked.Add(ref _currentSizeBytes, -size);
        }

        public T ExecuteLocked<T>(Func<T> action)
        {
            lock (_lock)
            {
                return action();
            }
        }

        public void ExecuteLocked(Action action)
        {
            lock (_lock)
            {
                action();
            }
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _currentSizeBytes, 0);
        }
    }

    internal static class CacheCleanupSkeleton
    {
        public static long CleanupExpired<TKey, TValue>(
            ConcurrentDictionary<TKey, TValue> cache,
            Func<TValue, bool> isExpired,
            Func<TValue, long> getSize,
            CacheSizeTracker sizeTracker)
        {
            var expiredKeys = cache.Where(kv => isExpired(kv.Value))
                                   .Select(kv => kv.Key)
                                   .ToList();

            long removedSize = 0;
            sizeTracker.ExecuteLocked(() =>
            {
                foreach (var key in expiredKeys)
                {
                    if (cache.TryRemove(key, out var entry))
                    {
                        removedSize += getSize(entry);
                    }
                }
                sizeTracker.Subtract(removedSize);
            });
            return removedSize;
        }

        public static long EvictLru<TKey, TValue>(
            ConcurrentDictionary<TKey, TValue> cache,
            Func<TValue, DateTime> getLastAccessed,
            Func<TValue, long> getSize,
            int evictCount,
            CacheSizeTracker sizeTracker)
        {
            var oldEntries = cache.OrderBy(kv => getLastAccessed(kv.Value))
                                  .Take(evictCount)
                                  .ToList();

            long removedSize = 0;
            sizeTracker.ExecuteLocked(() =>
            {
                foreach (var kv in oldEntries)
                {
                    if (cache.TryRemove(kv.Key, out var entry))
                    {
                        removedSize += getSize(entry);
                    }
                }
                sizeTracker.Subtract(removedSize);
            });
            return removedSize;
        }
    }

    public class MatrixCacheManager : IDisposable
    {
        private class CacheEntry
        {
            public MatrixResult Value { get; }
            public DateTime ExpirationTime { get; }
            public DateTime LastAccessed { get; set; }
            public long Size { get; }

            public CacheEntry(MatrixResult value, DateTime expirationTime, long size)
            {
                Value = value;
                ExpirationTime = expirationTime;
                LastAccessed = DateTime.UtcNow;
                Size = size;
            }

            public bool IsExpired => DateTime.UtcNow > ExpirationTime;
        }

        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly long _maxSizeBytes;
        private readonly CacheSizeTracker _sizeTracker = new();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public MatrixCacheManager(long cacheSizeBytes = 100 * 1024 * 1024)
        {
            _maxSizeBytes = cacheSizeBytes;
            _cleanupTimer = new Timer(_ => SafeCleanupExpiredEntries(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public bool TryGet(string key, out MatrixResult result)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                entry.LastAccessed = DateTime.UtcNow;
                result = new MatrixResult(
                    entry.Value.Matrix,
                    entry.Value.ComputationTime,
                    entry.Value.Metadata,
                    IsCached: true);
                return true;
            }
            result = null;
            return false;
        }

        public void Set(string key, MatrixResult value, TimeSpan expiration)
        {
            var entrySize = EstimateSize(value);

            _sizeTracker.ExecuteLocked(() =>
            {
                if (_sizeTracker.CurrentSize + entrySize > _maxSizeBytes)
                {
                    CleanupCache();
                }

                if (_cache.TryRemove(key, out var oldEntry))
                {
                    _sizeTracker.Subtract(oldEntry.Size);
                }

                var newEntry = new CacheEntry(value, DateTime.UtcNow + expiration, entrySize);
                _cache[key] = newEntry;
                _sizeTracker.Add(entrySize);
            });
        }

        public static string GenerateKey<TInput>(string algorithmId, TInput input)
        {
            var inputStr = input?.ToString() ?? "null";
            var inputHash = inputStr.GetHashCode(StringComparison.Ordinal);
            return $"MatrixAlg_{algorithmId}_{inputHash:X8}";
        }

        public void Clear()
        {
            _sizeTracker.ExecuteLocked(() =>
            {
                _cache.Clear();
                _sizeTracker.Reset();
            });
        }

        public long CurrentSizeBytes => _sizeTracker.CurrentSize;

        public int Count => _cache.Count;

        private void CleanupCache()
        {
            CacheCleanupSkeleton.CleanupExpired(
                _cache,
                entry => entry.IsExpired,
                entry => entry.Size,
                _sizeTracker);

            if (_sizeTracker.CurrentSize > _maxSizeBytes)
            {
                CacheCleanupSkeleton.EvictLru(
                    _cache,
                    entry => entry.LastAccessed,
                    entry => entry.Size,
                    Math.Max(1, _cache.Count / 5),
                    _sizeTracker);
            }
        }

        private void CleanupExpiredEntries()
        {
            CacheCleanupSkeleton.CleanupExpired(
                _cache,
                entry => entry.IsExpired,
                entry => entry.Size,
                _sizeTracker);
        }

        private void SafeCleanupExpiredEntries()
        {
            try
            {
                CleanupExpiredEntries();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MatrixCache] Cleanup error: {ex.Message}");
            }
        }

        private static long EstimateSize(MatrixResult value)
        {
            return value.Matrix.RowCount * value.Matrix.ColumnCount * sizeof(double) + 1024;
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

    public class MatrixCalculator : IMatrixCalculator
    {
        private readonly ConcurrentDictionary<string, object> _algorithms = new();
        private bool _disposed;

        public async Task<MatrixResult> ComputeAsync<TInput>(
            string algorithmName,
            TInput input,
            MatrixComputationConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var algorithm = GetAlgorithm<TInput>(algorithmName);
            return await algorithm.ComputeAsync(
                input,
                config ?? MatrixComputationConfig.Default,
                progress,
                cancellationToken);
        }

        public void RegisterAlgorithm<TInput>(string name, IMatrixAlgorithm<TInput> algorithm)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Algorithm name cannot be null or empty", nameof(name));

            _algorithms[name] = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        }

        public bool HasAlgorithm(string name) => _algorithms.ContainsKey(name);
        public IReadOnlyCollection<string> GetAlgorithmNames() => _algorithms.Keys.ToArray();

        private IMatrixAlgorithm<TInput> GetAlgorithm<TInput>(string name)
        {
            if (!_algorithms.TryGetValue(name, out var algorithm))
                throw new KeyNotFoundException($"Algorithm '{name}' not found");

            if (algorithm is IMatrixAlgorithm<TInput> typedAlgorithm)
                return typedAlgorithm;

            throw new InvalidCastException($"Algorithm '{name}' has incorrect type");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _algorithms.Clear();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
