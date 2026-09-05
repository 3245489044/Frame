#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Data.Graph
{
    public class CachedGraphStorage : IGraphStorage
    {
        private readonly IGraphStorage _innerStorage;
        private readonly IGraphStorageCache _cache;
        private readonly TimeSpan _cacheDuration;

        public CachedGraphStorage(
            IGraphStorage innerStorage,
            IGraphStorageCache cache,
            TimeSpan? cacheDuration = null)
        {
            _innerStorage = innerStorage ?? throw new ArgumentNullException(nameof(innerStorage));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
        }

        public async Task InitializeAsync()
        {
            await _innerStorage.InitializeAsync();
        }

        public async Task StoreResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            string? symbol = null)
        {
            await _innerStorage.StoreResourceAsync(resourceId, metadata, symbol);
            
            var resource = new UniversalWorkflowFramework.Core.Models.Resource
            {
                Identifier = resourceId,
                Metadata = new Dictionary<string, object>(metadata),
                Symbol = symbol
            };
            await _cache.SetAsync(resourceId, resource);
        }

        public async Task<UniversalWorkflowFramework.Core.Models.Resource?> GetResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            var cached = await _cache.GetAsync(resourceId);
            if (cached != null)
            {
                return cached;
            }

            var resource = await _innerStorage.GetResourceAsync(resourceId);
            if (resource != null)
            {
                await _cache.SetAsync(resourceId, resource);
            }

            return resource;
        }

        public async Task DeleteResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            await _innerStorage.DeleteResourceAsync(resourceId);
            await _cache.InvalidateAsync(resourceId);
        }

        public async Task<List<UniversalWorkflowFramework.Core.Models.Resource>> GetResourcesByTypeAsync(string resourceType)
        {
            return await _innerStorage.GetResourcesByTypeAsync(resourceType);
        }

        public async Task<List<UniversalWorkflowFramework.Core.Models.Resource>> QueryResourcesAsync(
            string resourceType,
            Dictionary<string, object>? filters = null,
            int? limit = null,
            int? offset = null,
            CancellationToken cancellationToken = default)
        {
            return await _innerStorage.QueryResourcesAsync(resourceType, filters, limit, offset, cancellationToken);
        }

        public async Task<List<UniversalWorkflowFramework.Core.Models.Resource>> SearchResourcesAsync(
            string keyword,
            List<string>? searchFields = null,
            int maxResults = 100,
            CancellationToken cancellationToken = default)
        {
            return await _innerStorage.SearchResourcesAsync(keyword, searchFields, maxResults, cancellationToken);
        }

        public async Task<long> CountResourcesAsync(
            string resourceType,
            Dictionary<string, object>? filters = null,
            CancellationToken cancellationToken = default)
        {
            return await _innerStorage.CountResourcesAsync(resourceType, filters, cancellationToken);
        }

        public async Task<Dictionary<string, long>> CountByTypeGroupAsync(
            CancellationToken cancellationToken = default)
        {
            return await _innerStorage.CountByTypeGroupAsync(cancellationToken);
        }

        public async Task BulkStoreResourcesAsync(
            List<(UniversalWorkflowFramework.Core.Models.ResourceIdentifier ResourceId,
                   Dictionary<string, object> Metadata,
                   string? Symbol)> resources,
            CancellationToken cancellationToken = default)
        {
            await _innerStorage.BulkStoreResourcesAsync(resources, cancellationToken);
            await _cache.InvalidateAllAsync(cancellationToken);
        }

        public async Task BulkDeleteResourcesAsync(
            List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier> resourceIds,
            CancellationToken cancellationToken = default)
        {
            await _innerStorage.BulkDeleteResourcesAsync(resourceIds, cancellationToken);
            foreach (var id in resourceIds)
            {
                await _cache.InvalidateAsync(id, cancellationToken);
            }
        }

        public async Task<bool> ExistsAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default)
        {
            return await _innerStorage.ExistsAsync(resourceId, cancellationToken);
        }

        public async Task UpdateResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default)
        {
            await _innerStorage.UpdateResourceAsync(resourceId, metadata, cancellationToken);
            await _cache.InvalidateAsync(resourceId, cancellationToken);
        }

        public Abstractions.Models.CacheStatistics GetCacheStatistics()
        {
            return _cache.GetStatistics();
        }
    }

    public static class CachedGraphStorageFactory
    {
        public static IGraphStorage Create(
            IGraphStorage innerStorage,
            IGraphStorageCache cache,
            TimeSpan? cacheDuration = null)
        {
            return new CachedGraphStorage(innerStorage, cache, cacheDuration);
        }
    }
}