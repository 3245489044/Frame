
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFramework.Models;

namespace GraphFramework.Snapshots
{
    public class GraphSnapshot<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public string SnapshotId { get; }

        public string GraphId { get; }

        public int Version { get; }

        public IReadOnlyDictionary<TKey, TNode> Nodes { get; }

        public IReadOnlyList<SnapshotEdge<TKey>> Edges { get; }

        public int NodeCount => Nodes.Count;

        public int EdgeCount => Edges.Count;

        public DateTime CreatedAt { get; }

        public IReadOnlyList<TKey> NodeKeys => Nodes.Keys.ToList().AsReadOnly();

        public GraphSnapshot(
            string snapshotId,
            string graphId,
            int version,
            IReadOnlyDictionary<TKey, TNode> nodes,
            IReadOnlyList<SnapshotEdge<TKey>> edges)
        {
            SnapshotId = PortGuard.Required(snapshotId, nameof(snapshotId));
            GraphId = PortGuard.Required(graphId, nameof(graphId));
            Version = version;
            Nodes = PortGuard.Required(nodes, nameof(nodes));
            Edges = PortGuard.Required(edges, nameof(edges));
            CreatedAt = DateTime.UtcNow;
        }

        public static GraphSnapshot<TKey, TNode, TEdge> CreateFrom(
            IGraph<TKey, TNode, TEdge> graph,
            string? graphId = null,
            int version = 1)
        {
            graph = PortGuard.Required(graph, nameof(graph));

            var nodes = new Dictionary<TKey, TNode>();
            foreach (var nodeKey in graph.GetAllNodeKeys())
            {
                var nodeData = graph.GetNodeData(nodeKey);
                if (nodeData != null)
                    nodes[nodeKey] = nodeData;
            }

            var edges = new List<SnapshotEdge<TKey>>();
            var edgeSet = new HashSet<(TKey source, TKey target)>();
            foreach (var nodeKey in graph.GetAllNodeKeys())
            {
                foreach (var neighborKey in graph.GetNeighbors(nodeKey))
                {
                    var edgeKey = (nodeKey, neighborKey);
                    if (edgeSet.Add(edgeKey))
                    {
                        var edgeId = Guid.NewGuid().ToString("N");
                        edges.Add(new SnapshotEdge<TKey>(edgeId, nodeKey, neighborKey, true));
                    }
                }
            }

            return new GraphSnapshot<TKey, TNode, TEdge>(
                Guid.NewGuid().ToString("N"),
                graphId ?? "graph",
                version,
                nodes,
                edges.AsReadOnly());
        }

        public static GraphSnapshot<TKey, TNode, TEdge> CreateFrom(
            string graphId,
            int version,
            IReadOnlyDictionary<TKey, TNode> nodes,
            IReadOnlyList<SnapshotEdge<TKey>> edges)
        {
            return new GraphSnapshot<TKey, TNode, TEdge>(
                Guid.NewGuid().ToString("N"),
                graphId,
                version,
                nodes,
                edges);
        }

        public TNode? GetNodeData(TKey nodeKey)
        {
            return Nodes.TryGetValue(nodeKey, out var node) ? node : null;
        }

        public IReadOnlyList<TKey> GetNextNodeKeys(TKey nodeKey)
        {
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.SourceNodeKey, nodeKey))
                .Select(e => e.TargetNodeKey)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<TKey> GetPreviousNodeKeys(TKey nodeKey)
        {
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.TargetNodeKey, nodeKey))
                .Select(e => e.SourceNodeKey)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<SnapshotEdge<TKey>> GetIncomingEdges(TKey nodeKey)
        {
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.TargetNodeKey, nodeKey))
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<SnapshotEdge<TKey>> GetOutgoingEdges(TKey nodeKey)
        {
            return Edges
                .Where(e => EqualityComparer<TKey>.Default.Equals(e.SourceNodeKey, nodeKey))
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<TKey> GetEntryNodeKeys()
        {
            var hasIncoming = Edges.Select(e => e.TargetNodeKey).ToHashSet();
            return Nodes.Keys.Where(k => !hasIncoming.Contains(k)).ToList().AsReadOnly();
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

        public IGraph<TKey, TNode, TEdge> ToGraph(IGraph<TKey, TNode, TEdge> targetGraph)
        {
            foreach (var kvp in Nodes)
            {
                targetGraph.AddNode(kvp.Key, kvp.Value);
            }
            foreach (var edge in Edges)
            {
                targetGraph.AddEdge(
                    edge.EdgeId,
                    edge.SourceNodeKey,
                    edge.TargetNodeKey,
                    edge.IsDirected,
                    default(TEdge),
                    1.0);
            }
            return targetGraph;
        }

        private static TKey KeyFactory(string s)
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

        public override string ToString() =>
            $"Snapshot[{SnapshotId}] Graph={GraphId} v{Version} Nodes={NodeCount} Edges={EdgeCount}";
    }

    public class SnapshotEdge<TKey>
        where TKey : notnull
    {
        public string EdgeId { get; }

        public TKey SourceNodeKey { get; }

        public TKey TargetNodeKey { get; }

        public bool IsDirected { get; }

        public SnapshotEdge(string edgeId, TKey sourceNodeKey, TKey targetNodeKey, bool isDirected = true)
        {
            EdgeId = PortGuard.Required(edgeId, nameof(edgeId));
            SourceNodeKey = sourceNodeKey;
            TargetNodeKey = targetNodeKey;
            IsDirected = isDirected;
        }

        public override string ToString() =>
            $"{EdgeId}: {SourceNodeKey} {(IsDirected ? "->" : "--")} {TargetNodeKey}";
    }
}
