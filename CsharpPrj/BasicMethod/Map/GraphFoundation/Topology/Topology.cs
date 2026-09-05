
#nullable enable

using GraphFoundation.Abstractions;

namespace GraphFoundation.Topology
{
    public class TopologyResult<TKey>
    {
        public IReadOnlyList<TKey> SortedNodes { get; }
        public bool HasCycle { get; }
        public IReadOnlyList<TKey>? CycleNodes { get; }

        public TopologyResult(IReadOnlyList<TKey> sortedNodes, bool hasCycle = false, IReadOnlyList<TKey>? cycleNodes = null)
        {
            SortedNodes = sortedNodes;
            HasCycle = hasCycle;
            CycleNodes = cycleNodes;
        }
    }

    public static class GraphTopology
    {
        private static (Dictionary<TKey, List<TKey>> Adjacency, Dictionary<TKey, int>? InDegree) BuildAdjacency<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, bool trackInDegree)
            where TKey : notnull
            where TNodeData : class
        {
            var adjacency = new Dictionary<TKey, List<TKey>>();
            var inDegree = trackInDegree ? new Dictionary<TKey, int>() : null;

            foreach (var key in graph.GetAllNodeKeys())
            {
                adjacency[key] = new List<TKey>();
                if (trackInDegree) inDegree![key] = 0;
            }

            foreach (var edgeId in graph.GetAllEdgeIds())
            {
                var source = graph.GetEdgeSource(edgeId);
                var target = graph.GetEdgeTarget(edgeId);

                if (adjacency.ContainsKey(source))
                    adjacency[source].Add(target);

                if (trackInDegree && inDegree!.ContainsKey(target))
                    inDegree[target]++;
            }

            return (adjacency, inDegree);
        }

        private static Dictionary<TKey, List<TKey>> BuildUndirectedAdjacency<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var adjacency = new Dictionary<TKey, List<TKey>>();

            foreach (var key in graph.GetAllNodeKeys())
                adjacency[key] = new List<TKey>();

            foreach (var edgeId in graph.GetAllEdgeIds())
            {
                var source = graph.GetEdgeSource(edgeId);
                var target = graph.GetEdgeTarget(edgeId);
                if (adjacency.ContainsKey(source))
                    adjacency[source].Add(target);
                if (adjacency.ContainsKey(target))
                    adjacency[target].Add(source);
            }

            return adjacency;
        }

        public static TopologyResult<TKey> TopologicalSort<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var (adjacency, inDegree) = BuildAdjacency(graph, trackInDegree: true);

            var queue = new Queue<TKey>();
            foreach (var kvp in inDegree!)
            {
                if (kvp.Value == 0)
                    queue.Enqueue(kvp.Key);
            }

            var sorted = new List<TKey>();
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sorted.Add(node);

                if (adjacency.TryGetValue(node, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        inDegree[neighbor]--;
                        if (inDegree[neighbor] == 0)
                            queue.Enqueue(neighbor);
                    }
                }
            }

            if (sorted.Count != graph.GetAllNodeKeys().Count)
            {
                var cycleNodes = inDegree.Where(kvp => kvp.Value > 0)
                                         .Select(kvp => kvp.Key)
                                         .ToList();
                return new TopologyResult<TKey>(sorted, true, cycleNodes);
            }

            return new TopologyResult<TKey>(sorted);
        }

        public static TopologyResult<TKey> TopologicalSortDFS<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var visited = new HashSet<TKey>();
            var onPath = new HashSet<TKey>();
            var result = new List<TKey>();
            var cyclePath = new List<TKey>();
            var hasCycle = false;

            var (adjacency, _) = BuildAdjacency(graph, trackInDegree: false);

            void Dfs(TKey node)
            {
                visited.Add(node);
                onPath.Add(node);

                if (adjacency.TryGetValue(node, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (onPath.Contains(neighbor))
                        {
                            hasCycle = true;
                            if (cyclePath.Count == 0)
                            {
                                cyclePath.Add(neighbor);
                                cyclePath.Add(node);
                            }
                        }
                        if (!visited.Contains(neighbor))
                            Dfs(neighbor);
                    }
                }

                onPath.Remove(node);
                result.Insert(0, node);
            }

            foreach (var key in graph.GetAllNodeKeys())
            {
                if (!visited.Contains(key))
                    Dfs(key);
            }

            return new TopologyResult<TKey>(result, hasCycle, hasCycle ? cyclePath : null);
        }


        public static bool HasCycle<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var result = TopologicalSort(graph);
            return result.HasCycle;
        }

        public static IReadOnlyList<IReadOnlyList<TKey>> GetConnectedComponents<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var components = new List<IReadOnlyList<TKey>>();
            TraverseConnectedComponents(graph, component => components.Add(component.ToList()));
            return components;
        }

        public static void TraverseConnectedComponents<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            Action<IEnumerable<TKey>> componentCallback)
            where TKey : notnull
            where TNodeData : class
        {
            var visited = new HashSet<TKey>();
            var adjacency = BuildUndirectedAdjacency(graph);


            foreach (var key in graph.GetAllNodeKeys())
            {
                if (!visited.Contains(key))
                {
                    var component = new List<TKey>();
                    var stack = new Stack<TKey>();
                    stack.Push(key);

                    while (stack.Count > 0)
                    {
                        var node = stack.Pop();
                        if (visited.Add(node))
                        {
                            component.Add(node);
                            if (adjacency.TryGetValue(node, out var neighbors))
                            {
                                foreach (var neighbor in neighbors)
                                {
                                    if (!visited.Contains(neighbor))
                                        stack.Push(neighbor);
                                }
                            }
                        }
                    }

                    componentCallback(component);
                }
            }
        }

        public static IReadOnlyList<TKey>? ShortestPath<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, TKey from, TKey to)
            where TKey : notnull
            where TNodeData : class
        {
            if (!graph.ContainsNode(from) || !graph.ContainsNode(to))
                return null;

            var visited = new HashSet<TKey> { from };
            var queue = new Queue<TKey>();
            var parent = new Dictionary<TKey, TKey>();

            queue.Enqueue(from);
            parent[from] = from;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Equals(to))
                {
                    var path = new List<TKey>();
                    var node = to;
                    while (!node.Equals(from))
                    {
                        path.Add(node);
                        node = parent[node];
                    }
                    path.Add(from);
                    path.Reverse();
                    return path;
                }

                foreach (var edge in graph.GetOutgoingEdges(current))
                {
                    if (visited.Add(edge.Target))
                    {
                        parent[edge.Target] = current;
                        queue.Enqueue(edge.Target);
                    }
                }
            }

            return null;
        }
    }
}
