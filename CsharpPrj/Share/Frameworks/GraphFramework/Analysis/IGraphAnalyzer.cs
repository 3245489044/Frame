
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFramework.Models;

namespace GraphFramework.Analysis
{
    public interface IGraphAnalyzer<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        GraphStatistics<TKey> Analyze(IGraph<TKey, TNode, TEdge> graph);

        Result<List<TKey>> TopologicalSort(IGraph<TKey, TNode, TEdge> graph);

        List<List<TKey>> FindAllPaths(IGraph<TKey, TNode, TEdge> graph, TKey start, TKey end);

        List<Chain<TKey, TNode>> FindChains(IGraph<TKey, TNode, TEdge> graph);

        bool HasCycle(IGraph<TKey, TNode, TEdge> graph, out List<TKey>? cycleNodes);
    }

    public class GraphStatistics<TKey>
        where TKey : notnull
    {
        public int NodeCount { get; set; }

        public int EdgeCount { get; set; }

        public int ChainCount { get; set; }

        public bool HasCycle { get; set; }

        public double AverageOutDegree { get; set; }

        public double AverageInDegree { get; set; }

        public int EntryNodeCount { get; set; }

        public int ExitNodeCount { get; set; }

        public int IsolatedNodeCount { get; set; }

        public Dictionary<string, int> NodeTypeDistribution { get; set; } = new();

        public Dictionary<string, int> SymbolDistribution { get; set; } = new();

        public override string ToString() =>
            $"Stats: {NodeCount} nodes, {EdgeCount} edges, {ChainCount} chains, " +
            $"Cycle={HasCycle}, Entry={EntryNodeCount}, Exit={ExitNodeCount}";
    }

    public class DefaultGraphAnalyzer<TKey, TNode, TEdge> : IGraphAnalyzer<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public GraphStatistics<TKey> Analyze(IGraph<TKey, TNode, TEdge> graph)
        {
            var stats = new GraphStatistics<TKey>();
            var allKeys = graph.GetAllNodeKeys().ToList();

            stats.NodeCount = allKeys.Count;
            stats.EdgeCount = allKeys.Sum(k => graph.GetNeighbors(k).Count());

            var inDegree = new Dictionary<TKey, int>();
            var outDegree = new Dictionary<TKey, int>();

            foreach (var key in allKeys)
            {
                var neighbors = graph.GetNeighbors(key).ToList();
                outDegree[key] = neighbors.Count;
                foreach (var neighbor in neighbors)
                {
                    inDegree[neighbor] = inDegree.GetValueOrDefault(neighbor, 0) + 1;
                }
            }

            stats.AverageOutDegree = allKeys.Count > 0
                ? outDegree.Values.Average() : 0;
            stats.AverageInDegree = allKeys.Count > 0
                ? inDegree.Values.Average() : 0;

            stats.EntryNodeCount = allKeys.Count(k => inDegree.GetValueOrDefault(k, 0) == 0);
            stats.ExitNodeCount = allKeys.Count(k => outDegree.GetValueOrDefault(k, 0) == 0);
            stats.IsolatedNodeCount = allKeys.Count(k =>
                inDegree.GetValueOrDefault(k, 0) == 0 && outDegree.GetValueOrDefault(k, 0) == 0);

            stats.HasCycle = HasCycle(graph, out _);

            stats.ChainCount = FindChains(graph).Count;

            return stats;
        }

        public Result<List<TKey>> TopologicalSort(IGraph<TKey, TNode, TEdge> graph)
        {
            var allKeys = graph.GetAllNodeKeys().ToList();
            var inDegree = new Dictionary<TKey, int>();
            var adjacency = new Dictionary<TKey, List<TKey>>();

            foreach (var key in allKeys)
            {
                inDegree.TryAdd(key, 0);
                adjacency[key] = graph.GetNeighbors(key).ToList();
                foreach (var neighbor in adjacency[key])
                {
                    inDegree[neighbor] = inDegree.GetValueOrDefault(neighbor, 0) + 1;
                }
            }

            var queue = new Queue<TKey>(allKeys.Where(k => inDegree[k] == 0));
            var result = new List<TKey>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current);

                foreach (var neighbor in adjacency.GetValueOrDefault(current, new List<TKey>()))
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            if (result.Count != allKeys.Count)
                return Result<List<TKey>>.Failure("图中存在环，无法进行拓扑排序");

            return Result<List<TKey>>.Success(result);
        }

        public List<List<TKey>> FindAllPaths(IGraph<TKey, TNode, TEdge> graph, TKey start, TKey end)
        {
            var paths = new List<List<TKey>>();
            var visited = new HashSet<TKey>();
            var currentPath = new List<TKey>();

            void Dfs(TKey current)
            {
                visited.Add(current);
                currentPath.Add(current);

                if (EqualityComparer<TKey>.Default.Equals(current, end))
                {
                    paths.Add(new List<TKey>(currentPath));
                }
                else
                {
                    foreach (var neighbor in graph.GetNeighbors(current))
                    {
                        if (!visited.Contains(neighbor))
                            Dfs(neighbor);
                    }
                }

                currentPath.RemoveAt(currentPath.Count - 1);
                visited.Remove(current);
            }

            Dfs(start);
            return paths;
        }

        public List<Chain<TKey, TNode>> FindChains(IGraph<TKey, TNode, TEdge> graph)
        {
            var chains = new List<Chain<TKey, TNode>>();
            var allKeys = graph.GetAllNodeKeys().ToList();
            var visited = new HashSet<TKey>();

            var inDegree = new Dictionary<TKey, int>();
            foreach (var key in allKeys)
            {
                inDegree.TryAdd(key, 0);
                foreach (var neighbor in graph.GetNeighbors(key))
                {
                    inDegree[neighbor] = inDegree.GetValueOrDefault(neighbor, 0) + 1;
                }
            }

            var entryNodes = allKeys.Where(k => inDegree.GetValueOrDefault(k, 0) == 0).ToList();

            foreach (var entry in entryNodes)
            {
                TraverseChain(graph, entry, visited, chains);
            }

            foreach (var key in allKeys)
            {
                if (!visited.Contains(key))
                {
                    TraverseChain(graph, key, visited, chains);
                }
            }

            return chains;
        }

        private void TraverseChain(IGraph<TKey, TNode, TEdge> graph, TKey start,
            HashSet<TKey> visited, List<Chain<TKey, TNode>> chains)
        {
            var chain = new Chain<TKey, TNode>();
            var current = start;

            while (current != null && !visited.Contains(current))
            {
                visited.Add(current);
                var nodeData = graph.GetNodeData(current);
                if (nodeData != null)
                    chain.AllNodes.Add(nodeData);

                var neighbors = graph.GetNeighbors(current).ToList();
                current = neighbors.Count == 1 ? neighbors[0] : default!;
            }

            if (chain.AllNodes.Count > 0)
            {
                chain.StartNode = chain.AllNodes.First();
                chain.EndNode = chain.AllNodes.Last();
                chains.Add(chain);
            }
        }

        public bool HasCycle(IGraph<TKey, TNode, TEdge> graph, out List<TKey>? cycleNodes)
        {
            var allKeys = graph.GetAllNodeKeys().ToList();
            var visited = new HashSet<TKey>();
            var inStack = new HashSet<TKey>();
            var parent = new Dictionary<TKey, TKey>();
            List<TKey>? foundCycle = null;

            bool Dfs(TKey current)
            {
                visited.Add(current);
                inStack.Add(current);

                foreach (var neighbor in graph.GetNeighbors(current))
                {
                    if (!visited.Contains(neighbor))
                    {
                        parent[neighbor] = current;
                        if (Dfs(neighbor)) return true;
                    }
                    else if (inStack.Contains(neighbor))
                    {
                        foundCycle = ReconstructCycle(current, neighbor, parent);
                        return true;
                    }
                }

                inStack.Remove(current);
                return false;
            }

            foreach (var key in allKeys)
            {
                if (!visited.Contains(key))
                {
                    if (Dfs(key))
                    {
                        cycleNodes = foundCycle;
                        return true;
                    }
                }
            }

            cycleNodes = null;
            return false;
        }

        private List<TKey> ReconstructCycle(TKey current, TKey neighbor, Dictionary<TKey, TKey> parent)
        {
            var cycle = new List<TKey>();
            var node = current;

            while (!EqualityComparer<TKey>.Default.Equals(node, neighbor))
            {
                cycle.Add(node);
                if (!parent.TryGetValue(node, out node!))
                    break;
            }
            cycle.Add(neighbor);
            cycle.Reverse();
            return cycle;
        }
    }
}
