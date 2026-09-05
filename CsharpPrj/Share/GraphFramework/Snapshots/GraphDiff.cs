
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphFramework.Snapshots
{
    public class GraphDiff<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public IReadOnlyList<DiffNode<TKey, TNode>> AddedNodes { get; }

        public IReadOnlyList<DiffNode<TKey, TNode>> RemovedNodes { get; }

        public IReadOnlyList<DiffNode<TKey, TNode>> ModifiedNodes { get; }

        public IReadOnlyList<DiffEdge<TKey>> AddedEdges { get; }

        public IReadOnlyList<DiffEdge<TKey>> RemovedEdges { get; }

        public bool HasChanges =>
            AddedNodes.Count > 0 || RemovedNodes.Count > 0 ||
            ModifiedNodes.Count > 0 || AddedEdges.Count > 0 ||
            RemovedEdges.Count > 0;

        public string Summary =>
            $"Diff: +{AddedNodes.Count}/-{RemovedNodes.Count}/~{ModifiedNodes.Count} nodes, +{AddedEdges.Count}/-{RemovedEdges.Count} edges";

        public GraphDiff(
            IReadOnlyList<DiffNode<TKey, TNode>> addedNodes,
            IReadOnlyList<DiffNode<TKey, TNode>> removedNodes,
            IReadOnlyList<DiffNode<TKey, TNode>> modifiedNodes,
            IReadOnlyList<DiffEdge<TKey>> addedEdges,
            IReadOnlyList<DiffEdge<TKey>> removedEdges)
        {
            AddedNodes = addedNodes ?? Array.Empty<DiffNode<TKey, TNode>>();
            RemovedNodes = removedNodes ?? Array.Empty<DiffNode<TKey, TNode>>();
            ModifiedNodes = modifiedNodes ?? Array.Empty<DiffNode<TKey, TNode>>();
            AddedEdges = addedEdges ?? Array.Empty<DiffEdge<TKey>>();
            RemovedEdges = removedEdges ?? Array.Empty<DiffEdge<TKey>>();
        }

        public static GraphDiff<TKey, TNode, TEdge> Compare(
            GraphSnapshot<TKey, TNode, TEdge>? before,
            GraphSnapshot<TKey, TNode, TEdge>? after)
        {
            var addedNodes = new List<DiffNode<TKey, TNode>>();
            var removedNodes = new List<DiffNode<TKey, TNode>>();
            var modifiedNodes = new List<DiffNode<TKey, TNode>>();
            var addedEdges = new List<DiffEdge<TKey>>();
            var removedEdges = new List<DiffEdge<TKey>>();

            var beforeNodes = before?.Nodes ?? new Dictionary<TKey, TNode>();
            var afterNodes = after?.Nodes ?? new Dictionary<TKey, TNode>();
            var beforeEdges = before?.Edges ?? new List<SnapshotEdge<TKey>>();
            var afterEdges = after?.Edges ?? new List<SnapshotEdge<TKey>>();

            foreach (var kvp in afterNodes)
            {
                if (!beforeNodes.ContainsKey(kvp.Key))
                    addedNodes.Add(new DiffNode<TKey, TNode>(kvp.Key, kvp.Value, DiffType.Added));
                else if (!Equals(beforeNodes[kvp.Key], kvp.Value))
                    modifiedNodes.Add(new DiffNode<TKey, TNode>(kvp.Key, kvp.Value, DiffType.Modified));
            }

            foreach (var kvp in beforeNodes)
            {
                if (!afterNodes.ContainsKey(kvp.Key))
                    removedNodes.Add(new DiffNode<TKey, TNode>(kvp.Key, kvp.Value, DiffType.Removed));
            }

            var afterEdgeSet = new HashSet<(TKey source, TKey target)>(
                afterEdges.Select(e => (e.SourceNodeKey, e.TargetNodeKey)));
            var beforeEdgeSet = new HashSet<(TKey source, TKey target)>(
                beforeEdges.Select(e => (e.SourceNodeKey, e.TargetNodeKey)));

            foreach (var edge in afterEdges)
            {
                var edgeKey = (edge.SourceNodeKey, edge.TargetNodeKey);
                if (!beforeEdgeSet.Contains(edgeKey))
                    addedEdges.Add(new DiffEdge<TKey>(edge, DiffType.Added));
            }

            foreach (var edge in beforeEdges)
            {
                var edgeKey = (edge.SourceNodeKey, edge.TargetNodeKey);
                if (!afterEdgeSet.Contains(edgeKey))
                    removedEdges.Add(new DiffEdge<TKey>(edge, DiffType.Removed));
            }

            return new GraphDiff<TKey, TNode, TEdge>(
                addedNodes, removedNodes, modifiedNodes,
                addedEdges, removedEdges);
        }

        public override string ToString() => Summary;
    }

    public enum DiffType
    {
        Added,
        Removed,
        Modified
    }

    public class DiffNode<TKey, TNode>
        where TKey : notnull
        where TNode : class
    {
        public TKey Key { get; }
        public TNode? Data { get; }
        public DiffType Type { get; }

        public DiffNode(TKey key, TNode? data, DiffType type)
        {
            Key = key;
            Data = data;
            Type = type;
        }

        public override string ToString() =>
            $"[{Type}] Node: {Key}";
    }

    public class DiffEdge<TKey>
        where TKey : notnull
    {
        public SnapshotEdge<TKey> Edge { get; }
        public DiffType Type { get; }

        public DiffEdge(SnapshotEdge<TKey> edge, DiffType type)
        {
            Edge = edge;
            Type = type;
        }

        public override string ToString() =>
            $"[{Type}] Edge: {Edge.EdgeId}";
    }
}
