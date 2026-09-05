
#nullable enable

namespace Modules.GraphProcessing.Snapshots
{
    public class GraphDiff<TNodeData, TEdgeData>
        where TNodeData : class
        where TEdgeData : class
    {
        private readonly GraphFoundation.Snapshots.GraphDiff<string, TNodeData, TEdgeData> _inner;

        public string OldSnapshotId => _inner.OldSnapshotId;

        public string NewSnapshotId => _inner.NewSnapshotId;

        public IReadOnlyList<string> AddedNodeKeys => _inner.AddedNodeKeys;

        public IReadOnlyList<string> RemovedNodeKeys => _inner.RemovedNodeKeys;

        public IReadOnlyDictionary<string, (GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? Old, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? New)> ModifiedNodes
            => _inner.ModifiedNodes;

        public IReadOnlyList<string> AddedEdgeIds => _inner.AddedEdgeIds;

        public IReadOnlyList<string> RemovedEdgeIds => _inner.RemovedEdgeIds;

        public bool HasChanges => _inner.HasChanges;

        public GraphDiff(
            string oldSnapshotId,
            string newSnapshotId,
            IReadOnlyList<string> addedNodeKeys,
            IReadOnlyList<string> removedNodeKeys,
            IReadOnlyDictionary<string, (GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? Old, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? New)> modifiedNodes,
            IReadOnlyList<string> addedEdgeIds,
            IReadOnlyList<string> removedEdgeIds)
        {
            _inner = new GraphFoundation.Snapshots.GraphDiff<string, TNodeData, TEdgeData>(
                oldSnapshotId,
                newSnapshotId,
                addedNodeKeys,
                removedNodeKeys,
                modifiedNodes,
                addedEdgeIds,
                removedEdgeIds);
        }

        public static GraphDiff<TNodeData, TEdgeData> Calculate(
            GraphSnapshot<TNodeData, TEdgeData> oldSnapshot,
            GraphSnapshot<TNodeData, TEdgeData> newSnapshot)
        {
            if (oldSnapshot == null) throw new ArgumentNullException(nameof(oldSnapshot));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));

            var innerDiff = GraphFoundation.Snapshots.GraphDiff<string, TNodeData, TEdgeData>.Calculate(
                oldSnapshot.AsFoundationSnapshot(),
                newSnapshot.AsFoundationSnapshot());

            return new GraphDiff<TNodeData, TEdgeData>(
                innerDiff.OldSnapshotId,
                innerDiff.NewSnapshotId,
                innerDiff.AddedNodeKeys,
                innerDiff.RemovedNodeKeys,
                innerDiff.ModifiedNodes,
                innerDiff.AddedEdgeIds,
                innerDiff.RemovedEdgeIds);
        }

        public GraphFoundation.Snapshots.GraphDiff<string, TNodeData, TEdgeData> AsFoundationDiff() => _inner;

        public override string ToString() => _inner.ToString();
    }
}
