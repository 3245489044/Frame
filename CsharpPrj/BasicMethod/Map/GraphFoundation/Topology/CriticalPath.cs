
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Navigation;

namespace GraphFoundation.Topology
{
    public static class CriticalPath
    {
        public static IReadOnlyList<TKey> FindLongestPath<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey startKey,
            TKey endKey)
            where TKey : notnull
            where TNodeData : class
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (startKey == null) throw new ArgumentNullException(nameof(startKey));
            if (endKey == null) throw new ArgumentNullException(nameof(endKey));

            var visited = new HashSet<TKey>();
            var path = new List<TKey>();
            var longestPath = new List<TKey>();

            FindLongestPathDfs(graph, startKey, endKey, visited, path, longestPath);
            return longestPath.AsReadOnly();
        }

        private static void FindLongestPathDfs<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey current,
            TKey end,
            HashSet<TKey> visited,
            List<TKey> path,
            List<TKey> longestPath)
            where TKey : notnull
            where TNodeData : class
        {
            visited.Add(current);
            path.Add(current);

            if (EqualityComparer<TKey>.Default.Equals(current, end))
            {
                if (path.Count > longestPath.Count)
                {
                    longestPath.Clear();
                    longestPath.AddRange(path);
                }
            }
            else
            {
                foreach (var neighbor in graph.GetSuccessorKeys(current))
                {
                    if (!visited.Contains(neighbor))
                        FindLongestPathDfs(graph, neighbor, end, visited, path, longestPath);
                }
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(current);
        }
    }
}
