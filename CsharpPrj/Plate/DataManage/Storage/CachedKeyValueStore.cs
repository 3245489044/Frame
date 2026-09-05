#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Storage
{
    using DataManage.Storage.Helpers;

    public sealed class CachedKeyValueStore : IKeyValueStore
    {
        private const int DefaultMaxCacheSize = 1000;

        private readonly IKeyValueStore _inner;
        private readonly ICacheEvictionPolicy<string> _evictionPolicy;
        private readonly ConcurrentDictionary<string, object> _cache = new();
        private readonly int _maxCacheSize;
        private readonly AsyncLock _cacheLock = new();

        public string Name => _inner.Name;

        public CachedKeyValueStore(IKeyValueStore inner, ICacheEvictionPolicy<string> evictionPolicy, int maxCacheSize = DefaultMaxCacheSize)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _evictionPolicy = evictionPolicy ?? throw new ArgumentNullException(nameof(evictionPolicy));
            _maxCacheSize = maxCacheSize;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => _inner.InitializeAsync(cancellationToken);

        private string GetCacheKey(string container, string keyField, object keyValue) =>
            $"{container}:{keyField}:{keyValue}";

        public async Task SaveAsync(string container, IDictionary<string, object> data, CancellationToken cancellationToken = default)
        {
            await _inner.SaveAsync(container, data, cancellationToken);
            InvalidateContainerCache(container);
        }

        public async Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList, CancellationToken cancellationToken = default)
        {
            await _inner.SaveBatchAsync(container, dataList, cancellationToken);
            InvalidateContainerCache(container);
        }

        private void InvalidateContainerCache(string container)
        {
            var prefix = $"{container}:";
            var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
                _evictionPolicy.OnRemove(key);
            }
        }

        public async Task<IEnumerable<IDictionary<string, object>>> LoadAllAsync(string container, CancellationToken cancellationToken = default)
        {
            return await _inner.LoadAllAsync(container, cancellationToken);
        }

        public async Task<int> DeleteAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            var result = await _inner.DeleteAsync(container, keyField, keyValue, cancellationToken);
            var cacheKey = GetCacheKey(container, keyField, keyValue);
            _cache.TryRemove(cacheKey, out _);
            _evictionPolicy.OnRemove(cacheKey);
            return result;
        }

        public async Task<bool> ExistsAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            var cacheKey = GetCacheKey(container, keyField, keyValue);
            if (_cache.ContainsKey(cacheKey))
            {
                _evictionPolicy.OnAccess(cacheKey);
                return true;
            }
            var exists = await _inner.ExistsAsync(container, keyField, keyValue, cancellationToken);
            if (exists)
            {
                await EnsureCacheCapacityAsync();
                _cache[cacheKey] = true;
                _evictionPolicy.OnAdd(cacheKey);
            }
            return exists;
        }

        private async Task EnsureCacheCapacityAsync()
        {
            using (await _cacheLock.LockAsync())
            {
                while (_cache.Count >= _maxCacheSize && _evictionPolicy.TryEvict(out var key))
                    _cache.TryRemove(key, out _);
            }
        }

        public void Dispose() => _inner.Dispose();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}