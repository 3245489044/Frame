
#nullable enable

using GraphFoundation.Abstractions;

namespace GraphFoundation.Snapshots
{
    public class GraphSnapshot<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public string SnapshotId { get; }

        public string GraphId { get; }

        public int Version { get; }

        public IReadOnlyDictionary<TKey, NodeDefinition<TKey, TNodeData>> Nodes { get; }

        public IReadOnlyList<EdgeDefinition<TKey>> Edges { get; }

        public int NodeCount => Nodes.Count;

        public int EdgeCount => Edges.Count;

        public DateTime CreatedAt { get; }

        public IReadOnlyList<TKey> NodeKeys => Nodes.Keys.ToList().AsReadOnly();

        public GraphSnapshot(
            string snapshotId,
            string graphId,
            int version,
            IReadOnlyDictionary<TKey, NodeDefinition<TKey, TNodeData>> nodes,
            IReadOnlyList<EdgeDefinition<TKey>> edges)
        {
            SnapshotId = snapshotId ?? throw new ArgumentNullException(nameof(snapshotId));
            GraphId = graphId ?? throw new ArgumentNullException(nameof(graphId));
            Version = version;
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            Edges = edges ?? throw new ArgumentNullException(nameof(edges));
            CreatedAt = DateTime.UtcNow;
        }

        public static GraphSnapshot<TKey, TNodeData, TEdgeData> CreateFrom(
            string graphId,
            int version,
            IReadOnlyDictionary<TKey, NodeDefinition<TKey, TNodeData>> nodes,
            IReadOnlyList<EdgeDefinition<TKey>> edges)
        {
            return new GraphSnapshot<TKey, TNodeData, TEdgeData>(
                Guid.NewGuid().ToString("N"),
                graphId,
                version,
                nodes,
                edges);
        }

        public static GraphSnapshot<TKey, TNodeData, TEdgeData> CreateFrom(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            string graphId = "graph",
            int version = 1)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var nodes = new Dictionary<TKey, NodeDefinition<TKey, TNodeData>>();
            foreach (var nodeKey in graph.GetAllNodeKeys())
            {
                var nodeData = graph.GetNodeData(nodeKey);
                var symbol = graph.GetNodeSymbol(nodeKey);
                nodes[nodeKey] = new NodeDefinition<TKey, TNodeData>(
                    nodeKey,
                    symbol ?? nodeKey.ToString() ?? "",
                    nodeData);
            }

            var edges = new List<EdgeDefinition<TKey>>();
            foreach (var nodeKey in graph.GetAllNodeKeys())
            {
                var outgoing = graph.GetOutgoingEdges(nodeKey);
                foreach (var edge in outgoing)
                {
                    var edgeId = edge.EdgeId;
                    if (!edges.Any(e => e.EdgeId == edgeId))
                    {
                        edges.Add(new EdgeDefinition<TKey>(
                            edgeId, edge.Source, edge.Target, edge.IsDirected, edge.Weight, edge.Data));
                    }
                }
            }

            return new GraphSnapshot<TKey, TNodeData, TEdgeData>(
                Guid.NewGuid().ToString("N"),
                graphId,
                version,
                nodes,
                edges.AsReadOnly());
        }

        public NodeDefinition<TKey, TNodeData>? GetNodeData(TKey nodeKey)
        {
            return Nodes.TryGetValue(nodeKey, out var node) ? node : null;
        }

        public string? GetNodeSymbol(TKey nodeKey)
        {
            return Nodes.TryGetValue(nodeKey, out var node) ? node.Symbol : null;
        }

        public NodeDefinition<TKey, TNodeData>? GetEntryNode()
        {
            return Nodes.Values.FirstOrDefault(n => GetIncomingEdges(n.NodeKey).Count == 0);
        }

        public IReadOnlyList<NodeDefinition<TKey, TNodeData>> GetEntryNodes()
        {
            return Nodes.Values
                .Where(n => GetIncomingEdges(n.NodeKey).Count == 0)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<TKey> GetNextNodeKeys(TKey nodeKey)
        {
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.SourceNodeKey, nodeKey))
                .Select(e => e.TargetNodeKey)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<NodeDefinition<TKey, TNodeData>> GetNextNodes(TKey nodeKey)
        {
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));
            return GetNextNodeKeys(nodeKey)
                .Select(k => GetNodeData(k))
                .Where(n => n != null)
                .ToList()!
                .AsReadOnly();
        }

        public IReadOnlyList<TKey> GetPreviousNodeKeys(TKey nodeKey)
        {
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.TargetNodeKey, nodeKey))
                .Select(e => e.SourceNodeKey)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<NodeDefinition<TKey, TNodeData>> GetPreviousNodes(TKey nodeKey)
        {
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));
            return GetPreviousNodeKeys(nodeKey)
                .Select(k => GetNodeData(k))
                .Where(n => n != null)
                .ToList()!
                .AsReadOnly();
        }

        public IReadOnlyList<EdgeDefinition<TKey>> GetIncomingEdges(TKey nodeKey)
        {
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.TargetNodeKey, nodeKey))
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<EdgeDefinition<TKey>> GetOutgoingEdges(TKey nodeKey)
        {
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.SourceNodeKey, nodeKey))
                .ToList()
                .AsReadOnly();
        }

        public bool Validate(out IReadOnlyList<string> errors)
        {
            var errorList = new List<string>();

            if (Nodes.Count == 0)
                errorList.Add("图中没有节点");

            foreach (var edge in Edges)
            {
                if (!Nodes.ContainsKey(edge.SourceNodeKey))
                    errorList.Add($"边的源节点不存在: {edge.SourceNodeKey}");
                if (!Nodes.ContainsKey(edge.TargetNodeKey))
                    errorList.Add($"边的目标节点不存在: {edge.TargetNodeKey}");
            }

            errors = errorList.AsReadOnly();
            return errors.Count == 0;
        }

        public override string ToString() =>
            $"Snapshot[{SnapshotId}] Graph={GraphId} v{Version} Nodes={NodeCount} Edges={EdgeCount}";
    }

    public class NodeDefinition<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        public TKey NodeKey { get; }

        public string Symbol { get; }

        public TNodeData? OriginalConfig { get; }

        public NodeDefinition(TKey nodeKey, string symbol, TNodeData? originalConfig)
        {
            NodeKey = nodeKey;
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            OriginalConfig = originalConfig;
        }

        public override string ToString() => $"{Symbol} ({NodeKey})";
    }

    public class EdgeDefinition<TKey>
        where TKey : notnull
    {
        public string EdgeId { get; }

        public TKey SourceNodeKey { get; }

        public TKey TargetNodeKey { get; }

        public bool IsDirected { get; }

        public double Weight { get; }

        public object? Data { get; }

        public EdgeDefinition(string edgeId, TKey sourceNodeKey, TKey targetNodeKey,
            bool isDirected = true, double weight = 1.0, object? data = null)
        {
            EdgeId = edgeId ?? throw new ArgumentNullException(nameof(edgeId));
            SourceNodeKey = sourceNodeKey;
            TargetNodeKey = targetNodeKey;
            IsDirected = isDirected;
            Weight = weight;
            Data = data;
        }

        public override string ToString() => $"{SourceNodeKey} -> {TargetNodeKey} ({EdgeId})";
    }
}
