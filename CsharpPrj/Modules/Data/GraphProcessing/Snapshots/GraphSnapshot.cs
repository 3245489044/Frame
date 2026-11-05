
#nullable enable

using GraphFoundation.Abstractions;
using FoundationSnapshot = GraphFoundation.Snapshots.GraphSnapshot<string, object, object>;
using FoundationNodeDef = GraphFoundation.Snapshots.NodeDefinition<string, object>;
using FoundationEdgeDef = GraphFoundation.Snapshots.EdgeDefinition<string>;

namespace Modules.GraphProcessing.Snapshots
{
    public class GraphSnapshot<TNodeData, TEdgeData>
        where TNodeData : class
        where TEdgeData : class
    {
        private readonly GraphFoundation.Snapshots.GraphSnapshot<string, TNodeData, TEdgeData> _inner;

        public string SnapshotId => _inner.SnapshotId;

        public string GraphId => _inner.GraphId;

        public int Version => _inner.Version;

        public IReadOnlyDictionary<string, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> Nodes => _inner.Nodes;

        public IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> Edges => _inner.Edges;

        public int NodeCount => _inner.NodeCount;

        public int EdgeCount => _inner.EdgeCount;

        public DateTime CreatedAt { get; }

        public IReadOnlyList<string> NodeKeys => _inner.NodeKeys;

        private readonly HashSet<string> _entryNodeKeys;

        private readonly IReadOnlyDictionary<string, int> _incomingEdgeCounts;

        public GraphSnapshot(
            string snapshotId,
            string graphId,
            int version,
            IReadOnlyDictionary<string, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> nodes,
            IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> edges)
        {
            _inner = new GraphFoundation.Snapshots.GraphSnapshot<string, TNodeData, TEdgeData>(snapshotId, graphId, version, nodes, edges);
            CreatedAt = DateTime.UtcNow;

            var incomingCounts = new Dictionary<string, int>();
            foreach (var nodeKey in nodes.Keys)
                incomingCounts[nodeKey] = 0;
            foreach (var edge in edges)
            {
                if (incomingCounts.ContainsKey(edge.TargetNodeKey))
                    incomingCounts[edge.TargetNodeKey]++;
            }
            _incomingEdgeCounts = new Dictionary<string, int>(incomingCounts);
            _entryNodeKeys = new HashSet<string>(
                incomingCounts.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        }

        public static GraphSnapshot<TNodeData, TEdgeData> CreateFrom(
            string graphId,
            int version,
            IReadOnlyDictionary<string, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> nodes,
            IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> edges)
        {
            return new GraphSnapshot<TNodeData, TEdgeData>(
                Guid.NewGuid().ToString("N"),
                graphId,
                version,
                nodes,
                edges);
        }

        public static GraphSnapshot<TNodeData, TEdgeData> CreateFrom(
            IReadOnlyGraph<string, TNodeData, TEdgeData> graph,
            string? graphId = null,
            int version = 1)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var nodes = new Dictionary<string, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>>();
            foreach (var nodeKey in graph.GetAllNodeKeys())
            {
                var nodeData = graph.GetNodeData(nodeKey);
                var symbol = nodeData?.ToString() ?? nodeKey;
                var nodeDef = new GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>(nodeKey, symbol, nodeData);
                nodes[nodeKey] = nodeDef;
            }

            var edges = new List<GraphFoundation.Snapshots.EdgeDefinition<string>>();
            var seenEdgeIds = new HashSet<string>();
            foreach (var nodeKey in graph.GetAllNodeKeys())
            {
                foreach (var outgoing in graph.GetOutgoingEdges(nodeKey))
                {
                    var edgeId = outgoing.EdgeId;
                    if (seenEdgeIds.Add(edgeId))
                    {
                        edges.Add(new GraphFoundation.Snapshots.EdgeDefinition<string>(
                            edgeId,
                            outgoing.Source.ToString()!,
                            outgoing.Target.ToString()!,
                            outgoing.IsDirected,
                            outgoing.Weight,
                            outgoing.Data));
                    }
                }
            }

            return new GraphSnapshot<TNodeData, TEdgeData>(
                Guid.NewGuid().ToString("N"),
                graphId ?? "graph",
                version,
                nodes,
                edges.AsReadOnly());
        }

        public GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? GetNodeData(string nodeKey)
        {
            return _inner.GetNodeData(nodeKey);
        }

        public string? GetNodeSymbol(string nodeKey)
        {
            return _inner.GetNodeSymbol(nodeKey);
        }

        public GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? GetEntryNode()
        {
            var entryKey = _entryNodeKeys.FirstOrDefault();
            return entryKey != null && Nodes.TryGetValue(entryKey, out var node) ? node : null;
        }

        public IReadOnlyList<GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> GetEntryNodes()
        {
            return _entryNodeKeys
                .Select(k => Nodes.TryGetValue(k, out var node) ? node : null)
                .Where(n => n != null)
                .ToList()!
                .AsReadOnly();
        }

        public IReadOnlyList<string> GetNextNodeKeys(string nodeKey)
        {
            return _inner.GetNextNodeKeys(nodeKey);
        }

        public IReadOnlyList<GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> GetNextNodes(string nodeKey)
        {
            return _inner.GetNextNodes(nodeKey);
        }

        public IReadOnlyList<string> GetPreviousNodeKeys(string nodeKey)
        {
            return _inner.GetPreviousNodeKeys(nodeKey);
        }

        public IReadOnlyList<GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> GetPreviousNodes(string nodeKey)
        {
            return _inner.GetPreviousNodes(nodeKey);
        }

        public IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> GetIncomingEdges(string nodeKey)
        {
            return _inner.GetIncomingEdges(nodeKey);
        }

        public IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> GetOutgoingEdges(string nodeKey)
        {
            return _inner.GetOutgoingEdges(nodeKey);
        }

        public bool Validate(out IReadOnlyList<string> errors)
        {
            return _inner.Validate(out errors);
        }

        public GraphFoundation.Snapshots.GraphSnapshot<string, TNodeData, TEdgeData> AsFoundationSnapshot() => _inner;

        public override string ToString() =>
            $"Snapshot[{SnapshotId}] Graph={GraphId} v{Version} Nodes={NodeCount} Edges={EdgeCount}";
    }
}
