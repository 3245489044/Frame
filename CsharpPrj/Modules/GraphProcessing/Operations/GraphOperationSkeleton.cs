
#nullable enable

using GraphFoundation.Abstractions;

namespace Modules.GraphProcessing.Operations
{
    public static class GraphOperationSkeleton<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public static void Merge(
            IGraph<TKey, TNodeData, TEdgeData> target,
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> source,
            Func<TNodeData?, TNodeData?, TNodeData?>? nodeDataMerge = null,
            Func<TEdgeData?, TEdgeData?, TEdgeData?>? edgeDataMerge = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (source == null) throw new ArgumentNullException(nameof(source));

            foreach (var nodeKey in source.GetAllNodeKeys())
            {
                if (target.ContainsNode(nodeKey))
                {
                    if (nodeDataMerge != null)
                    {
                        var existingData = target.GetNodeData(nodeKey);
                        var sourceData = source.GetNodeData(nodeKey);
                        var mergedData = nodeDataMerge(existingData, sourceData);
                        if (mergedData != null)
                            target.UpdateNode(nodeKey, mergedData);
                    }
                }
                else
                {
                    var nodeData = source.GetNodeData(nodeKey);
                    var symbol = source.GetNodeSymbol(nodeKey);
                    target.AddNode(nodeKey, nodeData!, symbol);
                }
            }

            foreach (var edgeId in source.GetAllEdgeIds())
            {
                if (!target.ContainsEdge(edgeId))
                {
                    var sourceKey = source.GetEdgeSource(edgeId);
                    var targetKey = source.GetEdgeTarget(edgeId);
                    var isDirected = source.GetEdgeIsDirected(edgeId);
                    var weight = source.GetEdgeWeight(edgeId);
                    var edgeData = source.GetEdgeData(edgeId);

                    target.AddEdge(edgeId, sourceKey, targetKey, isDirected, edgeData, weight);
                }
                else if (edgeDataMerge != null)
                {
                    var existingData = target.GetEdgeData(edgeId);
                    var sourceData = source.GetEdgeData(edgeId);
                    var mergedData = edgeDataMerge(existingData, sourceData);
                    if (mergedData != null)
                    {
                        var sourceKey = target.GetEdgeSource(edgeId);
                        var targetKey = target.GetEdgeTarget(edgeId);
                        var isDirected = target.GetEdgeIsDirected(edgeId);
                        var weight = target.GetEdgeWeight(edgeId);
                        target.RemoveEdge(edgeId);
                        target.AddEdge(edgeId, sourceKey, targetKey, isDirected, mergedData, weight);
                    }
                }
            }
        }

        public static void Filter(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> source,
            IGraph<TKey, TNodeData, TEdgeData> target,
            Func<TKey, TNodeData?, bool> predicate,
            Func<TNodeData?, TNodeData?>? nodeDataClone = null,
            Func<TEdgeData?, TEdgeData?>? edgeDataClone = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var selectedKeys = new HashSet<TKey>();
            foreach (var nodeKey in source.GetAllNodeKeys())
            {
                var nodeData = source.GetNodeData(nodeKey);
                if (predicate(nodeKey, nodeData))
                {
                    selectedKeys.Add(nodeKey);
                    var clonedData = nodeDataClone != null ? nodeDataClone(nodeData) : nodeData;
                    var symbol = source.GetNodeSymbol(nodeKey);
                    target.AddNode(nodeKey, clonedData!, symbol);
                }
            }

            foreach (var edgeId in source.GetAllEdgeIds())
            {
                var sourceKey = source.GetEdgeSource(edgeId);
                var targetKey = source.GetEdgeTarget(edgeId);

                if (selectedKeys.Contains(sourceKey) && selectedKeys.Contains(targetKey))
                {
                    var isDirected = source.GetEdgeIsDirected(edgeId);
                    var weight = source.GetEdgeWeight(edgeId);
                    var edgeData = source.GetEdgeData(edgeId);
                    var clonedEdgeData = edgeDataClone != null ? edgeDataClone(edgeData) : edgeData;

                    target.AddEdge(edgeId, sourceKey, targetKey, isDirected, clonedEdgeData, weight);
                }
            }
        }

        public static void Subgraph(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> source,
            IGraph<TKey, TNodeData, TEdgeData> target,
            IEnumerable<TKey> nodeKeys,
            Func<TNodeData?, TNodeData?>? nodeDataClone = null,
            Func<TEdgeData?, TEdgeData?>? edgeDataClone = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (nodeKeys == null) throw new ArgumentNullException(nameof(nodeKeys));

            var keySet = new HashSet<TKey>(nodeKeys);

            foreach (var key in keySet)
            {
                if (source.ContainsNode(key))
                {
                    var nodeData = source.GetNodeData(key);
                    var clonedData = nodeDataClone != null ? nodeDataClone(nodeData) : nodeData;
                    var symbol = source.GetNodeSymbol(key);
                    target.AddNode(key, clonedData!, symbol);
                }
            }

            foreach (var edgeId in source.GetAllEdgeIds())
            {
                var sourceKey = source.GetEdgeSource(edgeId);
                var targetKey = source.GetEdgeTarget(edgeId);

                if (keySet.Contains(sourceKey) && keySet.Contains(targetKey))
                {
                    var isDirected = source.GetEdgeIsDirected(edgeId);
                    var weight = source.GetEdgeWeight(edgeId);
                    var edgeData = source.GetEdgeData(edgeId);
                    var clonedEdgeData = edgeDataClone != null ? edgeDataClone(edgeData) : edgeData;

                    target.AddEdge(edgeId, sourceKey, targetKey, isDirected, clonedEdgeData, weight);
                }
            }
        }
    }
}
