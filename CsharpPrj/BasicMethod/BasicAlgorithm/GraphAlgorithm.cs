#region USINGS
using TConfig.ContextFrame.Abstractions;
using BasicMethod.ContextFrame.Core;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using GraphFoundation.Core.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
#endregion
#nullable enable




#region ==================== UniversalGraphStorage (通用图存储算法) ====================


namespace UniversalGraphStorage
{
#nullable enable

    #region Data Models

    public enum ReferenceType { Includes, DependsOn, InheritsFrom, Calls, References, Other }

    public class ResourceIdentifier
    {
        public string ResourceType { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string GetUniqueKey() => $"{ResourceType}:{Location}:{Id}";
        public ResourceIdentifier Clone() => new() { ResourceType = ResourceType, Id = Id, Name = Name, Location = Location };
    }

    public class ResourceData
    {
        public ResourceIdentifier Identifier { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    public class ReferenceData
    {
        public ReferenceType Type { get; set; }
        public string? CustomReferenceType { get; set; }
        public string? Location { get; set; }
        public string? Description { get; set; }
        public double Strength { get; set; } = 1.0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Interface

    public interface IGraphReferenceManager : IDisposable
    {
        Task InitializeAsync();
        Task AddResourceAsync(ResourceIdentifier resource, Dictionary<string, object>? metadata = null, string? symbol = null);
        Task UpdateResourceAsync(ResourceIdentifier resource, Dictionary<string, object>? metadata = null, string? symbol = null);
        Task RemoveResourceAsync(ResourceIdentifier resource);
        Task AddReferenceAsync(ResourceIdentifier source, ResourceIdentifier target, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0);
        Task RemoveReferenceAsync(ResourceIdentifier source, ResourceIdentifier target, ReferenceType referenceType);
        Task<ResourceData?> GetResourceAsync(ResourceIdentifier resource);
        Task<List<ResourceData>> GetAllResourcesAsync();
        Task<List<(ResourceIdentifier Source, ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength)>> GetReferencesAsync(ResourceIdentifier resource, bool includeIncoming = true);
        Task<List<ResourceIdentifier>> GetDependenciesAsync(ResourceIdentifier resource, int maxDepth = 5);
        Task<List<ResourceIdentifier>> GetDependentsAsync(ResourceIdentifier resource, int maxDepth = 5);
        Task<List<List<ResourceIdentifier>>> FindPathsAsync(ResourceIdentifier startResource, ResourceIdentifier endResource, int maxDepth = 10);
        Task<List<ResourceIdentifier>> FindShortestPathAsync(ResourceIdentifier startResource, ResourceIdentifier endResource);
        Task CleanupNonExistentResourcesAsync();
        bool ContainsResource(ResourceIdentifier resource);

        IGraphTopologyAnalyzer<string, ResourceData, ReferenceData> TopologyAnalyzer { get; }

        Task<List<ResourceData>> GetMiscellaneousResourcesAsync();

        Task<Dictionary<string, List<ResourceData>>> ClassifyMiscellaneousAsync();

        Task<MiscellaneousReport> GenerateMiscellaneousReportAsync();
    }

    #endregion

    #region Topology Analysis

    public interface IGraphTopologyAnalyzer<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        Dictionary<TKey, int> CalculateInDegrees();

        Dictionary<TKey, int> CalculateOutDegrees();

        List<TKey> FindOrphanedNodes();

        List<TKey> FindLeafNodes();

        List<TKey> FindIsolatedNodes();

        List<ConnectedComponent<TKey>> FindConnectedComponents();

        HashSet<TKey> FindReachableNodes(TKey root);

        List<TKey> FindUnreachableNodes(TKey root);
    }

    public class ConnectedComponent<TKey>
    {
        public int ComponentId { get; set; }
        public List<TKey> Nodes { get; set; } = new();
        public int Size => Nodes.Count;
        public bool IsIsolated { get; set; }
    }

    public class MiscellaneousReport
    {
        public int TotalResources { get; set; }
        public int TotalReferences { get; set; }
        public int MiscellaneousCount { get; set; }
        public int IsolatedCount { get; set; }
        public int ComponentCount { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new();
        public List<ConnectedComponent<string>> Components { get; set; } = new();
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
    }

    public class GraphTopologyAnalyzer<TKey, TNodeData, TEdgeData> : IGraphTopologyAnalyzer<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;

        public GraphTopologyAnalyzer(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public Dictionary<TKey, int> CalculateInDegrees()
        {
            var inDegrees = new Dictionary<TKey, int>();
            foreach (var key in _graph.GetAllNodeKeys())
                inDegrees[key] = 0;

            foreach (var key in _graph.GetAllNodeKeys())
            {
                foreach (var edge in EnumerateOutgoingEdges(key))
                {
                    if (inDegrees.ContainsKey(edge.Target))
                        inDegrees[edge.Target]++;
                }
            }
            return inDegrees;
        }

        public Dictionary<TKey, int> CalculateOutDegrees()
        {
            var outDegrees = new Dictionary<TKey, int>();
            foreach (var key in _graph.GetAllNodeKeys())
            {
                int count = 0;
                foreach (var _ in EnumerateOutgoingEdges(key))
                    count++;
                outDegrees[key] = count;
            }
            return outDegrees;
        }

        public List<TKey> FindOrphanedNodes()
        {
            var inDegrees = CalculateInDegrees();
            return inDegrees.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();
        }

        public List<TKey> FindLeafNodes()
        {
            var outDegrees = CalculateOutDegrees();
            return outDegrees.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();
        }

        public List<TKey> FindIsolatedNodes()
        {
            var inDegrees = CalculateInDegrees();
            var outDegrees = CalculateOutDegrees();
            return inDegrees.Where(kvp => kvp.Value == 0)
                .Join(outDegrees.Where(kvp => kvp.Value == 0),
                    inc => inc.Key,
                    outc => outc.Key,
                    (inc, outc) => inc.Key)
                .ToList();
        }

        public List<ConnectedComponent<TKey>> FindConnectedComponents()
        {
            var allKeys = _graph.GetAllNodeKeys();
            var visited = new HashSet<TKey>();
            var components = new List<ConnectedComponent<TKey>>();
            int componentId = 0;

            foreach (var key in allKeys)
            {
                if (visited.Contains(key)) continue;

                var component = new ConnectedComponent<TKey> { ComponentId = componentId++ };
                var queue = new Queue<TKey>();
                queue.Enqueue(key);
                visited.Add(key);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Nodes.Add(current);

                    foreach (var neighbor in _graph.GetNeighbors(current))
                    {
                        if (visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                components.Add(component);
            }

            var inDegrees = CalculateInDegrees();
            var outDegrees = CalculateOutDegrees();
            foreach (var component in components)
            {
                bool allIsolated = component.Nodes.All(n =>
                    (!inDegrees.TryGetValue(n, out var inD) || inD == 0) &&
                    (!outDegrees.TryGetValue(n, out var outD) || outD == 0));
                component.IsIsolated = allIsolated;
            }

            return components;
        }

        public HashSet<TKey> FindReachableNodes(TKey root)
        {
            var reachable = new HashSet<TKey>();
            var queue = new Queue<TKey>();
            queue.Enqueue(root);
            reachable.Add(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in _graph.GetNeighbors(current))
                {
                    if (reachable.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return reachable;
        }

        public List<TKey> FindUnreachableNodes(TKey root)
        {
            var reachable = FindReachableNodes(root);
            return _graph.GetAllNodeKeys().Where(k => !reachable.Contains(k)).ToList();
        }

        private IEnumerable<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> EnumerateOutgoingEdges(TKey source)
        {
            return _graph.GetOutgoingEdges(source);
        }
    }

    #endregion

    #region Implementation

    public class UniversalReferenceManager : IGraphReferenceManager
    {
        private static class Constants
        {
            public const string EdgeIdSeparator = "->";
            public static string BuildEdgeId(string sourceKey, string targetKey, ReferenceType referenceType) =>
                $"{sourceKey}{EdgeIdSeparator}{targetKey}{EdgeIdSeparator}{referenceType}";
        }

        private readonly ICoreGraph<string, ResourceData, ReferenceData> _graph;
        private readonly IContext _context;
        private bool _disposed;
        private IGraphTopologyAnalyzer<string, ResourceData, ReferenceData>? _topologyAnalyzer;

        public UniversalReferenceManager(IContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _graph = _context.GetOrAddService<ICoreGraph<string, ResourceData, ReferenceData>>(
                () => new CoreGraph<string, ResourceData, ReferenceData>());
        }

        internal ICoreGraph<string, ResourceData, ReferenceData> InnerGraph => _graph;

        public IGraphTopologyAnalyzer<string, ResourceData, ReferenceData> TopologyAnalyzer
        {
            get
            {
                if (_topologyAnalyzer == null)
                {
                    _topologyAnalyzer = new GraphTopologyAnalyzer<string, ResourceData, ReferenceData>(_graph);
                }
                return _topologyAnalyzer;
            }
        }

        public Task<List<ResourceData>> GetMiscellaneousResourcesAsync()
        {
            EnsureNotDisposed();
            var orphanedKeys = TopologyAnalyzer.FindOrphanedNodes();
            var resources = orphanedKeys
                .Select(k => _graph.GetNodeData(k))
                .Where(d => d != null)
                .Select(d => d!)
                .ToList();
            return Task.FromResult(resources);
        }

        public Task<Dictionary<string, List<ResourceData>>> ClassifyMiscellaneousAsync()
        {
            EnsureNotDisposed();
            var orphanedKeys = TopologyAnalyzer.FindOrphanedNodes();
            var classification = new Dictionary<string, List<ResourceData>>();

            foreach (var key in orphanedKeys)
            {
                var data = _graph.GetNodeData(key);
                if (data == null) continue;

                var type = data.Identifier.ResourceType ?? "Unknown";
                if (!classification.ContainsKey(type))
                    classification[type] = new List<ResourceData>();
                classification[type].Add(data);
            }

            return Task.FromResult(classification);
        }

        public Task<MiscellaneousReport> GenerateMiscellaneousReportAsync()
        {
            EnsureNotDisposed();
            var allKeys = _graph.GetAllNodeKeys();
            var allEdgeIds = _graph.GetAllEdgeIds();
            var orphanedKeys = TopologyAnalyzer.FindOrphanedNodes();
            var isolatedKeys = TopologyAnalyzer.FindIsolatedNodes();
            var components = TopologyAnalyzer.FindConnectedComponents();
            var classification = new Dictionary<string, int>();

            foreach (var key in orphanedKeys)
            {
                var data = _graph.GetNodeData(key);
                var type = data?.Identifier.ResourceType ?? "Unknown";
                if (!classification.ContainsKey(type))
                    classification[type] = 0;
                classification[type]++;
            }

            return Task.FromResult(new MiscellaneousReport
            {
                TotalResources = allKeys.Count,
                TotalReferences = allEdgeIds.Count,
                MiscellaneousCount = orphanedKeys.Count,
                IsolatedCount = isolatedKeys.Count,
                ComponentCount = components.Count,
                ByType = classification,
                Components = components,
                AnalysisTime = DateTime.UtcNow
            });
        }

        public Task InitializeAsync() => Task.CompletedTask;

        private void EnsureNotDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(UniversalReferenceManager)); }

        public Task AddResourceAsync(ResourceIdentifier resource, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            EnsureNotDisposed();
            var key = resource.GetUniqueKey();
            if (_graph.ContainsNode(key)) return Task.CompletedTask;
            var cloned = resource.Clone();
            _graph.AddNode(key, new ResourceData
            {
                Identifier = cloned,
                Metadata = metadata ?? new Dictionary<string, object>(),
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            }, symbol ?? cloned.Name);
            return Task.CompletedTask;
        }

        public Task UpdateResourceAsync(ResourceIdentifier resource, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            EnsureNotDisposed();
            var key = resource.GetUniqueKey();
            if (!_graph.ContainsNode(key)) return AddResourceAsync(resource, metadata, symbol);

            var existing = _graph.GetNodeData(key)!;
            var updated = new ResourceData
            {
                Identifier = resource.Clone(),
                Metadata = metadata ?? existing.Metadata,
                CreatedAt = existing.CreatedAt,
                LastModified = DateTime.UtcNow
            };
            _graph.UpdateNode(key, updated, symbol ?? existing.Identifier.Name);
            return Task.CompletedTask;
        }

        public Task RemoveResourceAsync(ResourceIdentifier resource)
        {
            EnsureNotDisposed();
            var key = resource.GetUniqueKey();
            if (_graph.ContainsNode(key)) _graph.RemoveNode(key);
            return Task.CompletedTask;
        }

        public Task<ResourceData?> GetResourceAsync(ResourceIdentifier resource)
        {
            EnsureNotDisposed();
            return Task.FromResult(_graph.GetNodeData(resource.GetUniqueKey()));
        }

        public Task<List<ResourceData>> GetAllResourcesAsync()
        {
            EnsureNotDisposed();
            return Task.FromResult(_graph.GetAllNodeKeys().Select(key => _graph.GetNodeData(key)).Where(d => d != null).Select(d => d!).ToList());
        }

        public bool ContainsResource(ResourceIdentifier resource)
        {
            EnsureNotDisposed();
            return _graph.ContainsNode(resource.GetUniqueKey());
        }

        public Task AddReferenceAsync(ResourceIdentifier source, ResourceIdentifier target, ReferenceType referenceType,
            string? location = null, string? description = null, double strength = 1.0)
        {
            EnsureNotDisposed();
            var sourceClone = source.Clone();
            var targetClone = target.Clone();
            var sourceKey = sourceClone.GetUniqueKey();
            var targetKey = targetClone.GetUniqueKey();
            string edgeId = Constants.BuildEdgeId(sourceKey, targetKey, referenceType);

            if (!_graph.ContainsNode(sourceKey))
                _graph.AddNode(sourceKey, new ResourceData { Identifier = sourceClone, Metadata = new(), CreatedAt = DateTime.UtcNow, LastModified = DateTime.UtcNow }, sourceClone.Name);
            if (!_graph.ContainsNode(targetKey))
                _graph.AddNode(targetKey, new ResourceData { Identifier = targetClone, Metadata = new(), CreatedAt = DateTime.UtcNow, LastModified = DateTime.UtcNow }, targetClone.Name);
            _graph.AddEdge(edgeId, sourceKey, targetKey, true,
                new ReferenceData { Type = referenceType, Location = location, Description = description, Strength = strength, CreatedAt = DateTime.UtcNow },
                strength);

            return Task.CompletedTask;
        }

        public Task RemoveReferenceAsync(ResourceIdentifier source, ResourceIdentifier target, ReferenceType referenceType)
        {
            EnsureNotDisposed();
            string edgeId = Constants.BuildEdgeId(source.GetUniqueKey(), target.GetUniqueKey(), referenceType);
            if (_graph.ContainsEdge(edgeId)) _graph.RemoveEdge(edgeId);
            return Task.CompletedTask;
        }

        public Task<List<(ResourceIdentifier Source, ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength)>> GetReferencesAsync(ResourceIdentifier resource, bool includeIncoming = true)
        {
            EnsureNotDisposed();
            var key = resource.GetUniqueKey();
            var references = new List<(ResourceIdentifier, ResourceIdentifier, ReferenceType, string?, string?, double)>();

            foreach (var edge in _graph.GetOutgoingEdges(key))
                if (_graph.GetNodeData(edge.Target)?.Identifier is { } targetRes)
                    references.Add((resource, targetRes, edge.Data?.Type ?? ReferenceType.Other, edge.Data?.Location, edge.Data?.Description, edge.Weight));

            if (includeIncoming)
                foreach (var edge in _graph.GetIncomingEdges(key))
                    if (_graph.GetNodeData(edge.Source)?.Identifier is { } sourceRes)
                        references.Add((sourceRes, resource, edge.Data?.Type ?? ReferenceType.Other, edge.Data?.Location, edge.Data?.Description, edge.Weight));

            return Task.FromResult(references);
        }

        public Task<List<ResourceIdentifier>> GetDependenciesAsync(ResourceIdentifier resource, int maxDepth = 5)
        {
            EnsureNotDisposed();
            var startKey = resource.GetUniqueKey();
            var result = new List<ResourceIdentifier>();
            var visited = new HashSet<string> { startKey };
            var queue = new Queue<(string Key, int Depth)>();
            queue.Enqueue((startKey, 0));
            var keysToFetch = new List<string>();

            while (queue.TryDequeue(out var cur) && cur.Depth < maxDepth)
                foreach (var edge in _graph.GetOutgoingEdges(cur.Key))
                    if (visited.Add(edge.Target))
                    {
                        keysToFetch.Add(edge.Target);
                        queue.Enqueue((edge.Target, cur.Depth + 1));
                    }

            foreach (var key in keysToFetch)
                if (_graph.GetNodeData(key)?.Identifier is { } id)
                    result.Add(id);

            return Task.FromResult(result);
        }

        public Task<List<ResourceIdentifier>> GetDependentsAsync(ResourceIdentifier resource, int maxDepth = 5)
        {
            EnsureNotDisposed();
            var startKey = resource.GetUniqueKey();
            var result = new List<ResourceIdentifier>();
            var visited = new HashSet<string> { startKey };
            var queue = new Queue<(string Key, int Depth)>();
            queue.Enqueue((startKey, 0));

            while (queue.TryDequeue(out var cur) && cur.Depth < maxDepth)
                foreach (var edge in _graph.GetIncomingEdges(cur.Key))
                    if (visited.Add(edge.Source) && _graph.GetNodeData(edge.Source)?.Identifier is { } sourceRes)
                    {
                        result.Add(sourceRes);
                        queue.Enqueue((edge.Source, cur.Depth + 1));
                    }

            return Task.FromResult(result);
        }

        public Task<List<List<ResourceIdentifier>>> FindPathsAsync(ResourceIdentifier startResource, ResourceIdentifier endResource, int maxDepth = 10)
        {
            EnsureNotDisposed();
            var startKey = startResource.GetUniqueKey();
            var endKey = endResource.GetUniqueKey();
            var paths = new List<List<ResourceIdentifier>>();
            var currentPath = new List<string> { startKey };
            var visited = new HashSet<string> { startKey };

            void Dfs(string current, int depth)
            {
                if (depth > maxDepth) return;
                if (current == endKey)
                {
                    var resourcePath = currentPath.Select(k => _graph.GetNodeData(k)?.Identifier).Where(id => id != null).Select(id => id!).ToList();
                    if (resourcePath.Count == currentPath.Count) paths.Add(resourcePath);
                    return;
                }
                foreach (var edge in _graph.GetOutgoingEdges(current))
                    if (visited.Add(edge.Target))
                    {
                        currentPath.Add(edge.Target);
                        Dfs(edge.Target, depth + 1);
                        currentPath.RemoveAt(currentPath.Count - 1);
                        visited.Remove(edge.Target);
                    }
            }

            Dfs(startKey, 1);
            return Task.FromResult(paths);
        }

        public Task<List<ResourceIdentifier>> FindShortestPathAsync(ResourceIdentifier startResource, ResourceIdentifier endResource)
        {
            EnsureNotDisposed();
            var startKey = startResource.GetUniqueKey();
            var endKey = endResource.GetUniqueKey();
            if (startKey == endKey)
                return Task.FromResult(new List<ResourceIdentifier> { startResource });

            var visited = new HashSet<string> { startKey };
            var queue = new Queue<string>();
            var predecessor = new Dictionary<string, string>();

            queue.Enqueue(startKey);
            bool found = false;

            while (queue.Count > 0 && !found)
            {
                var current = queue.Dequeue();
                foreach (var edge in _graph.GetOutgoingEdges(current))
                {
                    if (visited.Add(edge.Target))
                    {
                        predecessor[edge.Target] = current;
                        if (edge.Target == endKey)
                        {
                            found = true;
                            break;
                        }
                        queue.Enqueue(edge.Target);
                    }
                }
            }

            if (!found)
                return Task.FromResult(new List<ResourceIdentifier>());

            var pathKeys = new List<string>();
            var step = endKey;
            while (step != null)
            {
                pathKeys.Add(step);
                if (!predecessor.TryGetValue(step, out var prev))
                    break;
                step = prev;
            }
            pathKeys.Reverse();

            var result = pathKeys
                .Select(k => _graph.GetNodeData(k)?.Identifier)
                .Where(id => id != null)
                .Select(id => id!)
                .ToList();

            if (result.Count != pathKeys.Count)
                return Task.FromResult(new List<ResourceIdentifier>());

            return Task.FromResult(result);
        }

        public Task CleanupNonExistentResourcesAsync() => Task.CompletedTask;

        public void Dispose()
        {
            if (!_disposed)
            {
                _graph.Dispose();
                _disposed = true;
            }
        }
    }

    #endregion

    #region Factory

    public static class UniversalReferenceManagerFactory
    {
        public static IGraphReferenceManager Create() => new UniversalReferenceManager(new ContextBase());
    }

    #endregion
}

#endregion


