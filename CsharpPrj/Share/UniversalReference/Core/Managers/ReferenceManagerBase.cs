#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Modules.GraphProcessing.Algorithms;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.Core.Managers
{
    public abstract class ReferenceManagerBase : IUniversalReferenceManager
    {
        protected readonly Dictionary<string, Resource> _resources = new();
        protected readonly List<ReferenceRecord> _references = new();
        protected readonly object _lock = new();

        // 图算法下沉至 GraphProcessing.Algorithms（单源事实）。均无状态，可安全静态共享。
        private static readonly DefaultShortestPathFinder<string> _shortestPathFinder = new();
        private static readonly DefaultPathFinder<string> _pathFinder = new();

        #region IUniversalReferenceManager

        public abstract Task InitializeAsync();

        public virtual Task AddResourceAsync(Core.Models.ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            lock (_lock)
            {
                var key = GetResourceKey(resourceId);
                var resource = new Resource
                {
                    Identifier = resourceId,
                    Metadata = metadata ?? new Dictionary<string, object>(),
                    Symbol = symbol
                };
                _resources[key] = resource;
            }
            return Task.CompletedTask;
        }

        public virtual Task UpdateResourceAsync(Core.Models.ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            lock (_lock)
            {
                var key = GetResourceKey(resourceId);
                if (_resources.TryGetValue(key, out var existing))
                {
                    if (metadata != null)
                    {
                        foreach (var kvp in metadata)
                            existing.Metadata[kvp.Key] = kvp.Value;
                    }
                    if (symbol != null)
                        existing.Symbol = symbol;
                }
            }
            return Task.CompletedTask;
        }

        public Task<Resource?> GetResourceAsync(Core.Models.ResourceIdentifier resourceId)
        {
            lock (_lock)
            {
                var key = GetResourceKey(resourceId);
                if (_resources.TryGetValue(key, out var resource))
                {
                    return Task.FromResult<Resource?>(resource);
                }
            }
            return Task.FromResult<Resource?>(null);
        }

        public Task<List<Resource>> GetAllResourcesAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(_resources.Values.ToList());
            }
        }

        public virtual Task RemoveResourceAsync(Core.Models.ResourceIdentifier resourceId)
        {
            lock (_lock)
            {
                var key = GetResourceKey(resourceId);
                _resources.Remove(key);
                _references.RemoveAll(r =>
                    (r.SourceType == resourceId.ResourceType && r.SourceId == resourceId.Id) ||
                    (r.TargetType == resourceId.ResourceType && r.TargetId == resourceId.Id));
            }
            return Task.CompletedTask;
        }

        public virtual Task AddReferenceAsync(Core.Models.ResourceIdentifier sourceResourceId, Core.Models.ResourceIdentifier targetResourceId, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0)
        {
            lock (_lock)
            {
                var record = new ReferenceRecord
                {
                    SourceType = sourceResourceId.ResourceType,
                    SourceId = sourceResourceId.Id,
                    TargetType = targetResourceId.ResourceType,
                    TargetId = targetResourceId.Id,
                    RelationshipType = referenceType.ToString(),
                    Location = location,
                    Description = description,
                    Strength = strength
                };
                _references.Add(record);
            }
            return Task.CompletedTask;
        }

        public virtual Task RemoveReferenceAsync(Core.Models.ResourceIdentifier sourceResourceId, Core.Models.ResourceIdentifier targetResourceId, ReferenceType referenceType)
        {
            lock (_lock)
            {
                _references.RemoveAll(r => r.SourceType == sourceResourceId.ResourceType && r.SourceId == sourceResourceId.Id &&
                                           r.TargetType == targetResourceId.ResourceType && r.TargetId == targetResourceId.Id &&
                                           r.RelationshipType == referenceType.ToString());
            }
            return Task.CompletedTask;
        }

        private static (Core.Models.ResourceIdentifier Source, Core.Models.ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming) BuildReferenceTuple(ReferenceRecord r, bool isIncoming)
        {
            return (
                new Core.Models.ResourceIdentifier { ResourceType = r.SourceType, Id = r.SourceId },
                new Core.Models.ResourceIdentifier { ResourceType = r.TargetType, Id = r.TargetId },
                Enum.TryParse<ReferenceType>(r.RelationshipType, out var type) ? type : ReferenceType.Other,
                r.Location,
                r.Description,
                r.Strength,
                isIncoming
            );
        }

        public Task<List<(Core.Models.ResourceIdentifier Source, Core.Models.ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>> GetReferencesAsync(Core.Models.ResourceIdentifier resourceId, bool includeIncoming = true)
        {
            lock (_lock)
            {
                var result = new List<(Core.Models.ResourceIdentifier Source, Core.Models.ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>();

                foreach (var r in _references)
                {
                    if (r.SourceType == resourceId.ResourceType && r.SourceId == resourceId.Id)
                    {
                        result.Add(BuildReferenceTuple(r, isIncoming: false));
                    }
                    if (includeIncoming && r.TargetType == resourceId.ResourceType && r.TargetId == resourceId.Id)
                    {
                        result.Add(BuildReferenceTuple(r, isIncoming: true));
                    }
                }
                return Task.FromResult(result);
            }
        }

        public Task<List<Core.Models.ResourceIdentifier>> GetDependenciesAsync(Core.Models.ResourceIdentifier resourceId, int maxDepth = 5)
        {
            lock (_lock)
            {
                var visited = new HashSet<string>();
                var result = new List<Core.Models.ResourceIdentifier>();
                GetDependenciesRecursive(resourceId, maxDepth, visited, result);
                return Task.FromResult(result);
            }
        }

        public Task<List<Core.Models.ResourceIdentifier>> GetDependentsAsync(Core.Models.ResourceIdentifier resourceId, int maxDepth = 5)
        {
            lock (_lock)
            {
                var visited = new HashSet<string>();
                var result = new List<Core.Models.ResourceIdentifier>();
                GetDependentsRecursive(resourceId, maxDepth, visited, result);
                return Task.FromResult(result);
            }
        }

        public Task<List<List<Core.Models.ResourceIdentifier>>> FindPathsAsync(Core.Models.ResourceIdentifier startResourceId, Core.Models.ResourceIdentifier endResourceId, int maxDepth = 10)
        {
            lock (_lock)
            {
                var startKey = GetResourceKey(startResourceId);
                var endKey = GetResourceKey(endResourceId);
                // 下沉至 GraphProcessing.Algorithms.DefaultPathFinder（修正原 FindPathsRecursive 环守卫缺陷）。
                var paths = _pathFinder.FindAllPaths(startKey, endKey, GetOutgoingKeys, maxDepth);
                return Task.FromResult(paths.Select(p => p.Select(KeyToResourceIdentifier).ToList()).ToList());
            }
        }

        public Task<List<Core.Models.ResourceIdentifier>> FindShortestPathAsync(Core.Models.ResourceIdentifier startResourceId, Core.Models.ResourceIdentifier endResourceId)
        {
            lock (_lock)
            {
                var startKey = GetResourceKey(startResourceId);
                var endKey = GetResourceKey(endResourceId);
                // 下沉至 GraphProcessing.Algorithms.DefaultShortestPathFinder（BFS，语义与手写版一致，含 start==end 与不可达返回空）。
                var path = _shortestPathFinder.FindShortestPath(startKey, endKey, GetOutgoingKeys);
                return Task.FromResult(path.Select(KeyToResourceIdentifier).ToList());
            }
        }

        public Task<List<Resource>> GetResourcesByTypeAsync(string resourceType)
        {
            lock (_lock)
            {
                var result = string.IsNullOrEmpty(resourceType)
                    ? _resources.Values.ToList()
                    : _resources.Values
                        .Where(r => r.Identifier.ResourceType == resourceType)
                        .ToList();
                return Task.FromResult(result);
            }
        }

        public virtual Task CleanupNonExistentResourcesAsync()
        {
            lock (_lock)
            {
                var validResourceKeys = new HashSet<string>(_resources.Keys);
                _references.RemoveAll(r =>
                {
                    var sourceKey = GetResourceKey(new Core.Models.ResourceIdentifier { ResourceType = r.SourceType, Id = r.SourceId });
                    var targetKey = GetResourceKey(new Core.Models.ResourceIdentifier { ResourceType = r.TargetType, Id = r.TargetId });
                    return !validResourceKeys.Contains(sourceKey) || !validResourceKeys.Contains(targetKey);
                });
            }
            return Task.CompletedTask;
        }


        public bool ContainsResource(Core.Models.ResourceIdentifier resourceId)
        {
            lock (_lock)
            {
                return _resources.ContainsKey(GetResourceKey(resourceId));
            }
        }

        #endregion

        #region 图算法

        // 收敛说明：FindPathsAsync / FindShortestPathAsync 已下沉至 GraphProcessing.Algorithms
        // （DefaultPathFinder / DefaultShortestPathFinder），见上方方法体。
        // 以下 GetDependencies/GetDependents 保留手写 DFS：GraphProcessing.IPathFinder.GetReachableNodes
        // 为「无深度上限」可达集，无法表达 maxDepth 语义，故深度受限遍历留在本处（模型层）。

        private void GetDependenciesRecursive(Core.Models.ResourceIdentifier resourceId, int depth, HashSet<string> visited, List<Core.Models.ResourceIdentifier> result)
        {
            if (depth <= 0) return;
            var key = GetResourceKey(resourceId);
            if (!visited.Add(key)) return;

            foreach (var r in _references)
            {
                if (r.SourceType == resourceId.ResourceType && r.SourceId == resourceId.Id)
                {
                    var targetId = new Core.Models.ResourceIdentifier { ResourceType = r.TargetType, Id = r.TargetId };
                    result.Add(targetId);
                    GetDependenciesRecursive(targetId, depth - 1, visited, result);
                }
            }
        }

        private void GetDependentsRecursive(Core.Models.ResourceIdentifier resourceId, int depth, HashSet<string> visited, List<Core.Models.ResourceIdentifier> result)
        {
            if (depth <= 0) return;
            var key = GetResourceKey(resourceId);
            if (!visited.Add(key)) return;

            foreach (var r in _references)
            {
                if (r.TargetType == resourceId.ResourceType && r.TargetId == resourceId.Id)
                {
                    var sourceId = new Core.Models.ResourceIdentifier { ResourceType = r.SourceType, Id = r.SourceId };
                    result.Add(sourceId);
                    GetDependentsRecursive(sourceId, depth - 1, visited, result);
                }
            }
        }

        #endregion

        #region 辅助方法

        protected static string GetResourceKey(Core.Models.ResourceIdentifier resourceId) => BuildKey(resourceId.ResourceType, resourceId.Id);

        private static string BuildKey(string? type, string? id) => $"{type ?? "?"}{ResourceIdentifier.UniqueKeySeparator}{id ?? "?"}";

        /// <summary>供 GraphProcessing 查找器使用的出边邻居键枚举（基于引用表）。</summary>
        private IEnumerable<string> GetOutgoingKeys(string key)
        {
            foreach (var r in _references)
            {
                if (BuildKey(r.SourceType, r.SourceId) == key)
                    yield return BuildKey(r.TargetType, r.TargetId);
            }
        }

        /// <summary>将图键（ResourceType:Id）映射回 <see cref="Core.Models.ResourceIdentifier"/>。</summary>
        private static Core.Models.ResourceIdentifier KeyToResourceIdentifier(string key)
        {
            var sepIndex = key.IndexOf(ResourceIdentifier.UniqueKeySeparator, StringComparison.Ordinal);
            if (sepIndex < 0)
                return new Core.Models.ResourceIdentifier { ResourceType = key, Id = string.Empty };
            return new Core.Models.ResourceIdentifier
            {
                ResourceType = key.Substring(0, sepIndex),
                Id = key.Substring(sepIndex + 1)
            };
        }

        protected class ReferenceRecord
        {
            public string SourceType { get; set; } = string.Empty;
            public string SourceId { get; set; } = string.Empty;
            public string TargetType { get; set; } = string.Empty;
            public string TargetId { get; set; } = string.Empty;
            public string RelationshipType { get; set; } = string.Empty;
            public string? Location { get; set; }
            public string? Description { get; set; }
            public double Strength { get; set; } = 1.0;
        }

        #endregion
    }
}
