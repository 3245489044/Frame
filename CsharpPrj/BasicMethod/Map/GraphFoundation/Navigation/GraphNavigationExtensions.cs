
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Topology;

namespace GraphFoundation.Navigation
{
    public static class GraphNavigationExtensions
    {
        public static IReadOnlyList<TKey> GetSuccessorKeys<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey nodeKey)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));

            return graph.GetOutgoingEdges(nodeKey)
                .Select(e => e.Target)
                .Distinct()
                .ToList();
        }

        public static IReadOnlyList<TKey> GetPredecessorKeys<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey nodeKey)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));

            return graph.GetIncomingEdges(nodeKey)
                .Select(e => e.Source)
                .Distinct()
                .ToList();
        }

        public static IReadOnlyList<TKey> GetEntryNodeKeys<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var allKeys = graph.GetAllNodeKeys();
            var hasIncoming = new HashSet<TKey>();

            foreach (var key in allKeys)
            {
                var incoming = graph.GetIncomingEdges(key);
                foreach (var edge in incoming)
                {
                    hasIncoming.Add(key);
                }
            }

            return allKeys.Where(k => !hasIncoming.Contains(k)).ToList();
        }

        public static IReadOnlyList<TKey> GetExitNodeKeys<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var allKeys = graph.GetAllNodeKeys();
            var hasOutgoing = new HashSet<TKey>();

            foreach (var key in allKeys)
            {
                var outgoing = graph.GetOutgoingEdges(key);
                if (outgoing.Count > 0)
                    hasOutgoing.Add(key);
            }

            return allKeys.Where(k => !hasOutgoing.Contains(k)).ToList();
        }

        public static List<List<TKey>> GetAllPaths<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var entryNodes = graph.GetEntryNodeKeys();
            var exitNodes = graph.GetExitNodeKeys();
            var allPaths = new List<List<TKey>>();

            foreach (var entry in entryNodes)
            {
                foreach (var exit in exitNodes)
                {
                    var paths = FindAllPaths(graph, entry, exit);
                    allPaths.AddRange(paths);
                }
            }

            return allPaths;
        }

        public static List<List<TKey>> FindAllPaths<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey startNodeKey,
            TKey endNodeKey)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (startNodeKey == null || endNodeKey == null)
                return new List<List<TKey>>();

            var paths = new List<List<TKey>>();
            var visited = new HashSet<TKey>();
            var currentPath = new List<TKey>();

            DfsFindPaths(graph, startNodeKey, endNodeKey, visited, currentPath, paths);

            return paths;
        }

        private static void DfsFindPaths<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey current,
            TKey target,
            HashSet<TKey> visited,
            List<TKey> currentPath,
            List<List<TKey>> paths)
            where TKey : notnull
            where TNodeData : class
        {
            visited.Add(current);
            currentPath.Add(current);

            if (EqualityComparer<TKey>.Default.Equals(current, target))
            {
                paths.Add(new List<TKey>(currentPath));
            }
            else
            {
                var successors = graph.GetSuccessorKeys(current);
                foreach (var next in successors)
                {
                    if (!visited.Contains(next))
                    {
                        DfsFindPaths(graph, next, target, visited, currentPath, paths);
                    }
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
            visited.Remove(current);
        }

        public static IReadOnlyList<TKey> TopologicalSort<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey? startNodeKey = default)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var result = GraphTopology.TopologicalSort(graph);
            var sorted = result.SortedNodes.ToList();

            if (startNodeKey != null && !EqualityComparer<TKey>.Default.Equals(startNodeKey, default(TKey)!))
            {
                var startIndex = sorted.IndexOf(startNodeKey);
                if (startIndex > 0)
                {
                    var beforeStart = sorted.Take(startIndex).ToList();
                    sorted.RemoveRange(0, startIndex);
                    sorted.AddRange(beforeStart);
                }
            }

            return sorted;
        }
    }
}
