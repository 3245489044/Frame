
#nullable enable

namespace GraphFoundation.Snapshots
{
    public class GraphDiff<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public string OldSnapshotId { get; }

        public string NewSnapshotId { get; }

        public IReadOnlyList<TKey> AddedNodeKeys { get; }

        public IReadOnlyList<TKey> RemovedNodeKeys { get; }

        public IReadOnlyDictionary<TKey, (NodeDefinition<TKey, TNodeData>? Old, NodeDefinition<TKey, TNodeData>? New)> ModifiedNodes { get; }

        public IReadOnlyList<string> AddedEdgeIds { get; }

        public IReadOnlyList<string> RemovedEdgeIds { get; }

        public bool HasChanges =>
            AddedNodeKeys.Count > 0 || RemovedNodeKeys.Count > 0 ||
            ModifiedNodes.Count > 0 || AddedEdgeIds.Count > 0 || RemovedEdgeIds.Count > 0;

        public GraphDiff(
            string oldSnapshotId,
            string newSnapshotId,
            IReadOnlyList<TKey> addedNodeKeys,
            IReadOnlyList<TKey> removedNodeKeys,
            IReadOnlyDictionary<TKey, (NodeDefinition<TKey, TNodeData>? Old, NodeDefinition<TKey, TNodeData>? New)> modifiedNodes,
            IReadOnlyList<string> addedEdgeIds,
            IReadOnlyList<string> removedEdgeIds)
        {
            OldSnapshotId = oldSnapshotId ?? throw new ArgumentNullException(nameof(oldSnapshotId));
            NewSnapshotId = newSnapshotId ?? throw new ArgumentNullException(nameof(newSnapshotId));
            AddedNodeKeys = addedNodeKeys ?? Array.Empty<TKey>();
            RemovedNodeKeys = removedNodeKeys ?? Array.Empty<TKey>();
            ModifiedNodes = modifiedNodes ?? new Dictionary<TKey, (NodeDefinition<TKey, TNodeData>? Old, NodeDefinition<TKey, TNodeData>? New)>();
            AddedEdgeIds = addedEdgeIds ?? Array.Empty<string>();
            RemovedEdgeIds = removedEdgeIds ?? Array.Empty<string>();
        }

        public static GraphDiff<TKey, TNodeData, TEdgeData> Calculate(
            GraphSnapshot<TKey, TNodeData, TEdgeData> oldSnapshot,
            GraphSnapshot<TKey, TNodeData, TEdgeData> newSnapshot)
        {
            if (oldSnapshot == null) throw new ArgumentNullException(nameof(oldSnapshot));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));

            var oldNodeKeys = new HashSet<TKey>(oldSnapshot.Nodes.Keys);
            var newNodeKeys = new HashSet<TKey>(newSnapshot.Nodes.Keys);

            var addedNodeKeys = newNodeKeys.Except(oldNodeKeys).ToList();
            var removedNodeKeys = oldNodeKeys.Except(newNodeKeys).ToList();

            var modifiedNodes = new Dictionary<TKey, (NodeDefinition<TKey, TNodeData>? Old, NodeDefinition<TKey, TNodeData>? New)>();
            foreach (var key in oldNodeKeys.Intersect(newNodeKeys))
            {
                var oldNode = oldSnapshot.GetNodeData(key);
                var newNode = newSnapshot.GetNodeData(key);

                if (oldNode?.Symbol != newNode?.Symbol)
                {
                    modifiedNodes[key] = (oldNode, newNode);
                }
            }

            var oldEdgeIds = new HashSet<string>(oldSnapshot.Edges.Select(e => e.EdgeId));
            var newEdgeIds = new HashSet<string>(newSnapshot.Edges.Select(e => e.EdgeId));
            var addedEdgeIds = newEdgeIds.Except(oldEdgeIds).ToList();
            var removedEdgeIds = oldEdgeIds.Except(newEdgeIds).ToList();

            return new GraphDiff<TKey, TNodeData, TEdgeData>(
                oldSnapshot.SnapshotId,
                newSnapshot.SnapshotId,
                addedNodeKeys.AsReadOnly(),
                removedNodeKeys.AsReadOnly(),
                modifiedNodes,
                addedEdgeIds.AsReadOnly(),
                removedEdgeIds.AsReadOnly());
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (AddedNodeKeys.Count > 0) parts.Add($"+{AddedNodeKeys.Count}节点");
            if (RemovedNodeKeys.Count > 0) parts.Add($"-{RemovedNodeKeys.Count}节点");
            if (ModifiedNodes.Count > 0) parts.Add($"~{ModifiedNodes.Count}节点");
            if (AddedEdgeIds.Count > 0) parts.Add($"+{AddedEdgeIds.Count}边");
            if (RemovedEdgeIds.Count > 0) parts.Add($"-{RemovedEdgeIds.Count}边");
            return $"Diff[{OldSnapshotId} -> {NewSnapshotId}]: {string.Join(", ", parts)}";
        }
    }
}
