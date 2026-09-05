#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;
using DataManage.Abstractions.Models;

namespace DataManage.Data.Graph
{
    public class GraphStorageCache : IGraphStorageCache
    {
        private readonly Dictionary<string, (UniversalWorkflowFramework.Core.Models.Resource Resource, DateTime ExpiresAt)> _cache = new();
        private readonly Dictionary<string, HashSet<string>> _typeIndex = new();
        private readonly ICacheEvictionPolicy _evictionPolicy;
        private readonly TimeSpan _defaultExpiration;
        private readonly object _lock = new();
        private long _totalHits;
        private long _totalMisses;
        private long _totalEvictions;

        public GraphStorageCache(TimeSpan? defaultExpiration = null, ICacheEvictionPolicy? evictionPolicy = null)
        {
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(10);
            _evictionPolicy = evictionPolicy ?? new LruEvictionPolicy();
        }

        private string GetCacheKey(UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            return $"{resourceId.ResourceType}:{resourceId.Id}";
        }

        public Task<UniversalWorkflowFramework.Core.Models.Resource?> GetAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default)
        {
            var key = GetCacheKey(resourceId);

            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.ExpiresAt > DateTime.UtcNow)
                    {
                        _totalHits++;
                        return Task.FromResult<UniversalWorkflowFramework.Core.Models.Resource?>(entry.Resource);
                    }

                    RemoveFromTypeIndex(entry.Resource, key);
                    _cache.Remove(key);
                }

                _totalMisses++;
            }

            return Task.FromResult<UniversalWorkflowFramework.Core.Models.Resource?>(null);
        }

        public Task SetAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            UniversalWorkflowFramework.Core.Models.Resource resource,
            CancellationToken cancellationToken = default)
        {
            var key = GetCacheKey(resourceId);

            lock (_lock)
            {
                if (_cache.Count >= 1000)
                {
                    EvictExpired();
                    if (_cache.Count >= 1000)
                    {
                        var keysToEvict = _evictionPolicy.SelectKeysForEviction(_cache, 100);
                        foreach (var k in keysToEvict)
                        {
                            if (_cache.TryGetValue(k, out var entry))
                            {
                                RemoveFromTypeIndex(entry.Resource, k);
                            }
                            _cache.Remove(k);
                            _totalEvictions++;
                        }
                    }
                }

                var expiresAt = DateTime.UtcNow.Add(_defaultExpiration);
                _cache[key] = (resource, expiresAt);

                AddToTypeIndex(resource, key);
            }

            return Task.CompletedTask;
        }

        public Task InvalidateAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default)
        {
            var key = GetCacheKey(resourceId);

            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    RemoveFromTypeIndex(entry.Resource, key);
                    _cache.Remove(key);
                }
            }

            return Task.CompletedTask;
        }

        public Task InvalidateByTypeAsync(
            string resourceType,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_typeIndex.TryGetValue(resourceType, out var keys))
                {
                    foreach (var key in keys)
                    {
                        _cache.Remove(key);
                    }
                    _typeIndex.Remove(resourceType);
                }
            }

            return Task.CompletedTask;
        }

        public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _cache.Clear();
                _typeIndex.Clear();
            }

            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default)
        {
            var key = GetCacheKey(resourceId);

            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.ExpiresAt > DateTime.UtcNow)
                    {
                        return Task.FromResult(true);
                    }

                    RemoveFromTypeIndex(entry.Resource, key);
                    _cache.Remove(key);
                }
            }

            return Task.FromResult(false);
        }

        public CacheStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new CacheStatistics
                {
                    Hits = _totalHits,
                    Misses = _totalMisses,
                    Evictions = _totalEvictions,
                    CurrentSize = _cache.Count,
                    MaxSize = 1000
                };
            }
        }

        private void EvictExpired()
        {
            var now = DateTime.UtcNow;
            var keysToRemove = new List<string>();

            foreach (var (key, entry) in _cache)
            {
                if (entry.ExpiresAt <= now)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    RemoveFromTypeIndex(entry.Resource, key);
                }
                _cache.Remove(key);
            }
        }

        private void AddToTypeIndex(UniversalWorkflowFramework.Core.Models.Resource resource, string key)
        {
            var resourceType = resource.Identifier.ResourceType ?? "Unknown";
            if (!_typeIndex.ContainsKey(resourceType))
                _typeIndex[resourceType] = new HashSet<string>();
            _typeIndex[resourceType].Add(key);
        }

        private void RemoveFromTypeIndex(UniversalWorkflowFramework.Core.Models.Resource resource, string key)
        {
            var resourceType = resource.Identifier.ResourceType ?? "Unknown";
            if (_typeIndex.TryGetValue(resourceType, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _typeIndex.Remove(resourceType);
            }
        }
    }

    public class LruEvictionPolicy : ICacheEvictionPolicy
    {
        public List<string> SelectKeysForEviction(
            Dictionary<string, (UniversalWorkflowFramework.Core.Models.Resource Resource, DateTime ExpiresAt)> cache,
            int count)
        {
            return new List<string>(cache.Keys).GetRange(0, Math.Min(count, cache.Count));
        }
    }

    public static class GraphStorageCacheFactory
    {
        public static IGraphStorageCache Create(TimeSpan? expiration = null, ICacheEvictionPolicy? evictionPolicy = null)
        {
            return new GraphStorageCache(expiration, evictionPolicy);
        }
    }
}