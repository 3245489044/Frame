
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;

namespace GraphFramework.Ecs.Systems
{
    public struct TransformationResultComponent : IComponent
    {
        public bool Success { get; }
        public string Description { get; }
        public int NodesAdded { get; }
        public int EdgesAdded { get; }

        public TransformationResultComponent(bool success, string description, int nodesAdded = 0, int edgesAdded = 0)
        {
            Success = success;
            Description = description;
            NodesAdded = nodesAdded;
            EdgesAdded = edgesAdded;
        }
    }

    public class TransformationSystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly Func<TKey, TNodeData> _nodeDataFactory;
        private readonly Func<TKey, TKey, TEdgeData> _edgeDataFactory;
        private readonly Func<string, TKey> _keyFactory;

        public TransformationSystem(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<TKey, TNodeData> nodeDataFactory,
            Func<TKey, TKey, TEdgeData> edgeDataFactory,
            Func<string, TKey>? keyFactory = null)
        {
            _graph = graph;
            _nodeDataFactory = nodeDataFactory;
            _edgeDataFactory = edgeDataFactory;
            _keyFactory = keyFactory ?? DefaultKeyFactory;
        }

        public override void Update(float deltaTime)
        {
            foreach (var entity in EntityManager.GetAllEntities())
            {
                if (EntityManager.TryGetComponent<TransformationRequestComponent<TKey, TEdgeData>>(entity, out var request))
                {
                    ExecuteTransformation(entity, request);
                }
            }
        }

        private void ExecuteTransformation(
            Entity entity,
            TransformationRequestComponent<TKey, TEdgeData> request)
        {
            bool success = false;
            string description = "";
            int nodesAdded = 0, edgesAdded = 0;

            try
            {
                switch (request.Operation.ToLower())
                {
                    case "reverse":
                        int reversed = 0;
                        var allEdgeIds = _graph.GetAllEdgeIds().ToList();
                        var edgeInfos = new List<(string edgeId, TKey src, TKey tgt, bool isDir, double wgt, TEdgeData? dat)>();

                        foreach (var edgeId in allEdgeIds)
                        {
                            var source = _graph.GetEdgeSource(edgeId);
                            var target = _graph.GetEdgeTarget(edgeId);
                            var isDirected = _graph.GetEdgeIsDirected(edgeId);
                            var weight = _graph.GetEdgeWeight(edgeId);
                            var edgeData = _graph.GetEdgeData(edgeId);
                            edgeInfos.Add((edgeId, source, target, isDirected, weight, edgeData));
                        }

                        foreach (var info in edgeInfos)
                        {
                            _graph.RemoveEdge(info.edgeId);
                        }

                        int newIndex = 0;
                        foreach (var info in edgeInfos)
                        {
                            var newEdgeId = $"rev_{newIndex}_{info.tgt}_{info.src}";
                            _graph.AddEdge(newEdgeId, info.tgt, info.src, info.isDir, info.dat, info.wgt);
                            newIndex++;
                            reversed++;
                        }
                        description = $"Reversed edges: {reversed}";
                        success = true;
                        break;

                    case "subgraph":
                    case "extract":
                        if (request.Parameters != null && request.Parameters.Length > 0)
                        {
                            var subKeys = new HashSet<TKey>(request.Parameters.Select(k => _keyFactory(k)));
                            var allNodes = _graph.GetAllNodeKeys().ToList();
                            var nodesToRemove = allNodes.Where(k => !subKeys.Contains(k)).ToList();
                            foreach (var nodeKey in nodesToRemove)
                            {
                                _graph.RemoveNode(nodeKey);
                            }
                            description = $"Extracted subgraph: removed {nodesToRemove.Count} nodes, kept {subKeys.Count} nodes";
                            success = true;
                        }
                        break;

                    case "merge":
                        if (request.Parameters != null && request.Parameters.Length >= 2)
                        {
                            var targetKey = _keyFactory(request.Parameters[0]);
                            var sourceKey = _keyFactory(request.Parameters[1]);

                            var sourceEdges = _graph.GetAllEdgeIds()
                                .Where(e => _graph.GetEdgeSource(e).Equals(sourceKey) || _graph.GetEdgeTarget(e).Equals(sourceKey))
                                .ToList();

                            foreach (var edgeId in sourceEdges)
                            {
                                var edgeSource = _graph.GetEdgeSource(edgeId);
                                var edgeTarget = _graph.GetEdgeTarget(edgeId);
                                var isDirected = _graph.GetEdgeIsDirected(edgeId);
                                var weight = _graph.GetEdgeWeight(edgeId);
                                var edgeData = _graph.GetEdgeData(edgeId);

                                var newSource = edgeSource.Equals(sourceKey) ? targetKey : edgeSource;
                                var newTarget = edgeTarget.Equals(sourceKey) ? targetKey : edgeTarget;

                                if (!newSource.Equals(newTarget))
                                {
                                    _graph.RemoveEdge(edgeId);
                                    _graph.AddEdge(edgeId, newSource, newTarget, isDirected, edgeData, weight);
                                }
                            }

                            _graph.RemoveNode(sourceKey);
                            description = $"Merged {request.Parameters[1]} into {request.Parameters[0]}";
                            success = true;
                        }
                        break;

                    default:
                        description = $"Unknown operation: {request.Operation}";
                        break;
                }
            }
            catch (Exception ex)
            {
                description = $"Error: {ex.Message}";
            }

            var result = new TransformationResultComponent(success, description, nodesAdded, edgesAdded);

            if (EntityManager.HasComponent<TransformationResultComponent>(entity))
            {
                EntityManager.SetComponent(entity, result);
            }
            else
            {
                EntityManager.AddComponent(entity, result);
            }
        }

        private static TKey DefaultKeyFactory(string s)
        {
            if (typeof(TKey) == typeof(string))
                return (TKey)(object)s;
            if (typeof(TKey) == typeof(int))
                return (TKey)(object)int.Parse(s);
            if (typeof(TKey) == typeof(long))
                return (TKey)(object)long.Parse(s);
            if (typeof(TKey) == typeof(Guid))
                return (TKey)(object)Guid.Parse(s);
            throw new InvalidOperationException($"Cannot convert '{s}' to {typeof(TKey).Name}");
        }
    }

    public struct TransformationRequestComponent<TKey, TEdgeData> : IComponent
    {
        public string Operation { get; }

        public string[]? Parameters { get; }

        public TransformationRequestComponent(string operation, string[]? parameters = null)
        {
            Operation = operation;
            Parameters = parameters;
        }
    }
}


