
#nullable enable

using GraphFoundation.Abstractions;

namespace Modules.GraphProcessing.Operations
{
    public record GraphStatistics
    {
        public int NodeCount { get; init; }

        public int EdgeCount { get; init; }

        public double AverageOutDegree { get; init; }

        public int MaxDepth { get; init; }

        public bool HasCycle { get; init; }

        public int ComponentCount { get; init; }
    }

    public interface IGraphAnalyzer<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        bool HasCycle(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph);

        IReadOnlyList<TKey> FindCriticalPath(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, TKey startKey, TKey endKey);

        IReadOnlyList<IReadOnlyList<TKey>> FindConnectedComponents(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph);

        GraphStatistics Analyze(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph);
    }

    public class DefaultGraphAnalyzer<TKey, TNodeData, TEdgeData> : IGraphAnalyzer<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public bool HasCycle(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var result = GraphFoundation.Topology.GraphTopology.TopologicalSort(graph);
            return result.HasCycle;
        }

        public IReadOnlyList<TKey> FindCriticalPath(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey startKey,
            TKey endKey)
        {
            return GraphFoundation.Topology.CriticalPath.FindLongestPath(graph, startKey, endKey);
        }

        public IReadOnlyList<IReadOnlyList<TKey>> FindConnectedComponents(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var components = GraphFoundation.Topology.GraphTopology.GetConnectedComponents(graph);
            return components
                .Select(c => (IReadOnlyList<TKey>)c.ToList().AsReadOnly())
                .ToList().AsReadOnly();
        }

        public GraphStatistics Analyze(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var stats = new GraphFoundation.ErrorHandling.GraphStatistics<TKey, TNodeData, TEdgeData>(graph);
            var result = stats.Compute();

            int maxDepth = 0;
            try
            {
                var topoResult = GraphFoundation.Topology.GraphTopology.TopologicalSort(graph);
                if (topoResult.HasCycle)
                {
                    maxDepth = result.NodeCount;
                }
                else
                {
                    var entryNodes = GraphFoundation.Navigation.GraphNavigationExtensions
                        .GetEntryNodeKeys(graph);
                    var exitNodes = GraphFoundation.Navigation.GraphNavigationExtensions
                        .GetExitNodeKeys(graph);

                    foreach (var entry in entryNodes)
                    {
                        foreach (var exit in exitNodes)
                        {
                            var path = GraphFoundation.Topology.CriticalPath
                                .FindLongestPath(graph, entry, exit);
                            if (path.Count > maxDepth)
                                maxDepth = path.Count;
                        }
                    }

                    if (maxDepth == 0 && topoResult.SortedNodes.Count > 0)
                        maxDepth = topoResult.SortedNodes.Count;
                }
            }
            catch
            {
                maxDepth = result.NodeCount;
            }

            return new GraphStatistics
            {
                NodeCount = result.NodeCount,
                EdgeCount = result.EdgeCount,
                AverageOutDegree = Math.Round(result.AvgOutDegree, 2),
                MaxDepth = maxDepth,
                HasCycle = result.HasCycle,
                ComponentCount = result.ConnectedComponentCount
            };
        }
    }
}
