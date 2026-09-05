
#nullable enable

using GraphFoundation.Abstractions;

namespace GraphFoundation.Core
{
    public static class GraphCloneExtensions
    {
        public static bool CloneTo<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> source,
            IGraph<TKey, TNodeData, TEdgeData> target,
            Func<TNodeData?, TNodeData?>? cloneNodeData = null,
            Func<TEdgeData?, TEdgeData?>? cloneEdgeData = null)
            where TKey : notnull
            where TNodeData : class
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (target.GetAllNodeKeys().Count > 0)
                return false;

            using var transaction = target.BeginTransaction();
            try
            {
                foreach (var nodeKey in source.GetAllNodeKeys())
                {
                    var nodeData = source.GetNodeData(nodeKey);
                    var symbol = source.GetNodeSymbol(nodeKey);
                    var clonedData = cloneNodeData != null ? cloneNodeData(nodeData) : nodeData;
                    target.AddNode(nodeKey, clonedData!, symbol);
                }

                foreach (var edgeId in source.GetAllEdgeIds())
                {
                    var sourceKey = source.GetEdgeSource(edgeId);
                    var targetKey = source.GetEdgeTarget(edgeId);
                    var isDirected = source.GetEdgeIsDirected(edgeId);
                    var weight = source.GetEdgeWeight(edgeId);
                    var edgeData = source.GetEdgeData(edgeId);
                    var clonedEdgeData = cloneEdgeData != null ? cloneEdgeData(edgeData) : edgeData;

                    target.AddEdge(edgeId, sourceKey, targetKey, isDirected, clonedEdgeData, weight);
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public static ICoreGraph<TKey, TNodeData, TEdgeData> Clone<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> source,
            Func<TNodeData?, TNodeData?>? cloneNodeData = null,
            Func<TEdgeData?, TEdgeData?>? cloneEdgeData = null)
            where TKey : notnull
            where TNodeData : class
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var target = new CoreGraph<TKey, TNodeData, TEdgeData>();
            CloneTo(source, target, cloneNodeData, cloneEdgeData);
            return target;
        }
    }
}
