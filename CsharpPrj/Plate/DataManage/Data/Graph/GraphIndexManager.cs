#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;
using DataManage.Abstractions.Models;

namespace DataManage.Data.Graph
{
    public class GraphIndexManager : IGraphIndexManager
    {
        private readonly Dictionary<string, Dictionary<string, GraphIndexInfo>> _indexes = new();
        private readonly Dictionary<string, Dictionary<string, Dictionary<object, List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>>>> _indexData = new();
        private readonly object _lock = new();
        private readonly Func<string, string, CancellationToken, Task<List<UniversalWorkflowFramework.Core.Models.Resource>>> _getResourcesByTypeAsync;

        public GraphIndexManager(
            Func<string, string, CancellationToken, Task<List<UniversalWorkflowFramework.Core.Models.Resource>>>? getResourcesByTypeAsync = null)
        {
            _getResourcesByTypeAsync = getResourcesByTypeAsync ?? DefaultGetResourcesByTypeAsync;
        }

        private async Task<List<UniversalWorkflowFramework.Core.Models.Resource>> DefaultGetResourcesByTypeAsync(
            string resourceType, string propertyName, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return new List<UniversalWorkflowFramework.Core.Models.Resource>();
        }

        public Task CreateIndexAsync(string resourceType, string propertyName, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_indexes.ContainsKey(resourceType))
                    _indexes[resourceType] = new Dictionary<string, GraphIndexInfo>();

                if (!_indexes[resourceType].ContainsKey(propertyName))
                {
                    _indexes[resourceType][propertyName] = new GraphIndexInfo
                    {
                        ResourceType = resourceType,
                        PropertyName = propertyName,
                        CreatedAt = DateTime.UtcNow,
                        LastRebuiltAt = DateTime.UtcNow
                    };

                    if (!_indexData.ContainsKey(resourceType))
                        _indexData[resourceType] = new Dictionary<string, Dictionary<object, List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>>>();

                    _indexData[resourceType][propertyName] = new Dictionary<object, List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>>();
                }
            }

            return Task.CompletedTask;
        }

        public Task DropIndexAsync(string resourceType, string propertyName, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_indexes.ContainsKey(resourceType) && _indexes[resourceType].ContainsKey(propertyName))
                {
                    _indexes[resourceType].Remove(propertyName);
                }

                if (_indexData.ContainsKey(resourceType) && _indexData[resourceType].ContainsKey(propertyName))
                {
                    _indexData[resourceType].Remove(propertyName);
                }
            }

            return Task.CompletedTask;
        }

        public async Task RebuildIndexAsync(string resourceType, CancellationToken cancellationToken = default)
        {
            if (!_indexes.ContainsKey(resourceType))
                return;

            var properties = _indexes[resourceType].Keys.ToList();
            var resources = await _getResourcesByTypeAsync(resourceType, string.Empty, cancellationToken);

            lock (_lock)
            {
                foreach (var property in properties)
                {
                    if (_indexData.ContainsKey(resourceType) && _indexData[resourceType].ContainsKey(property))
                    {
                        _indexData[resourceType][property].Clear();

                        foreach (var resource in resources)
                        {
                            if (resource.Metadata.TryGetValue(property, out var value))
                            {
                                if (!_indexData[resourceType][property].ContainsKey(value))
                                    _indexData[resourceType][property][value] = new List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>();

                                _indexData[resourceType][property][value].Add(resource.Identifier);
                            }
                        }
                    }

                    if (_indexes[resourceType].ContainsKey(property))
                    {
                        _indexes[resourceType][property].LastRebuiltAt = DateTime.UtcNow;
                    }
                }
            }
        }

        public Task<bool> HasIndexAsync(string resourceType, string propertyName, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(
                    _indexes.ContainsKey(resourceType) &&
                    _indexes[resourceType].ContainsKey(propertyName));
            }
        }

        public Task<List<string>> GetIndexedPropertiesAsync(string resourceType, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_indexes.ContainsKey(resourceType))
                {
                    return Task.FromResult(_indexes[resourceType].Keys.ToList());
                }
            }

            return Task.FromResult(new List<string>());
        }

        public Task<List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>> LookupByIndexAsync(
            string propertyName,
            object value,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                foreach (var resourceIndexData in _indexData.Values)
                {
                    if (resourceIndexData.ContainsKey(propertyName) &&
                        resourceIndexData[propertyName].ContainsKey(value))
                    {
                        return Task.FromResult(resourceIndexData[propertyName][value]);
                    }
                }
            }

            return Task.FromResult(new List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>());
        }

        public Task<Dictionary<string, long>> GetIndexStatisticsAsync(CancellationToken cancellationToken = default)
        {
            var stats = new Dictionary<string, long>();

            lock (_lock)
            {
                foreach (var (resourceType, properties) in _indexes)
                {
                    foreach (var (propertyName, info) in properties)
                    {
                        var key = $"{resourceType}.{propertyName}";
                        if (_indexData.ContainsKey(resourceType) &&
                            _indexData[resourceType].ContainsKey(propertyName))
                        {
                            stats[key] = _indexData[resourceType][propertyName].Count;
                        }
                    }
                }
            }

            return Task.FromResult(stats);
        }

        public void UpdateIndex(string resourceType, string propertyName, object value, UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            lock (_lock)
            {
                if (_indexData.ContainsKey(resourceType) &&
                    _indexData[resourceType].ContainsKey(propertyName))
                {
                    if (!_indexData[resourceType][propertyName].ContainsKey(value))
                        _indexData[resourceType][propertyName][value] = new List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>();

                    if (!_indexData[resourceType][propertyName][value].Any(r => r.Id == resourceId.Id && r.ResourceType == resourceId.ResourceType))
                        _indexData[resourceType][propertyName][value].Add(resourceId);

                    if (_indexes[resourceType].ContainsKey(propertyName))
                        _indexes[resourceType][propertyName].EntryCount++;
                }
            }
        }

        public void RemoveFromIndex(string resourceType, string propertyName, object value, UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            lock (_lock)
            {
                if (_indexData.ContainsKey(resourceType) &&
                    _indexData[resourceType].ContainsKey(propertyName) &&
                    _indexData[resourceType][propertyName].ContainsKey(value))
                {
                    _indexData[resourceType][propertyName][value].RemoveAll(
                        r => r.Id == resourceId.Id && r.ResourceType == resourceId.ResourceType);

                    if (_indexes[resourceType].ContainsKey(propertyName))
                        _indexes[resourceType][propertyName].EntryCount--;
                }
            }
        }
    }

    public static class GraphIndexManagerFactory
    {
        public static IGraphIndexManager Create()
        {
            return new GraphIndexManager();
        }

        public static IGraphIndexManager Create(
            Func<string, string, CancellationToken, Task<List<UniversalWorkflowFramework.Core.Models.Resource>>> getResourcesByTypeAsync)
        {
            return new GraphIndexManager(getResourcesByTypeAsync);
        }
    }
}