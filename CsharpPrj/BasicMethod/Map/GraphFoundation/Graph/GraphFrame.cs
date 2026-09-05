
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.ErrorHandling;
using GraphFoundation.Storage;
using GraphFoundation.Topology;

namespace GraphFoundation.Core
{
    public static class GraphFrame
    {
        public static ICoreGraph<TKey, TNodeData, TEdgeData> Create<TKey, TNodeData, TEdgeData>(
            IErrorHandler? errorHandler = null)
            where TKey : notnull
            where TNodeData : class
            => new CoreGraph<TKey, TNodeData, TEdgeData>(null, errorHandler);

        public static ICoreGraph<TKey, TNodeData, TEdgeData> CreateWithStorage<TKey, TNodeData, TEdgeData>(
            IGraphStorage<TKey, TNodeData, TEdgeData> storage,
            IErrorHandler? errorHandler = null)
            where TKey : notnull
            where TNodeData : class
            => new CoreGraph<TKey, TNodeData, TEdgeData>(storage, errorHandler);

        public static ICoreGraph<TKey, TNodeData, TEdgeData> CreateWithDefaultErrorHandler<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
            => new CoreGraph<TKey, TNodeData, TEdgeData>(null, new DefaultErrorHandler());

        public static ICoreGraph<TKey, TNodeData, TEdgeData> CreateStrict<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
            => new CoreGraph<TKey, TNodeData, TEdgeData>(null, new StrictErrorHandler());

        public static GraphValidator<TKey, TNodeData, TEdgeData> CreateValidator<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => new(graph);

        public static GraphStatistics<TKey, TNodeData, TEdgeData> CreateStatistics<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => new(graph);

        public static TopologyResult<TKey> TopologicalSort<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => GraphTopology.TopologicalSort(graph);

        public static bool HasCycle<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => GraphTopology.HasCycle(graph);

        public static IReadOnlyList<IReadOnlyList<TKey>> GetConnectedComponents<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => GraphTopology.GetConnectedComponents(graph);

        public static IReadOnlyList<TKey>? ShortestPath<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, TKey from, TKey to)
            where TKey : notnull
            where TNodeData : class
            => GraphTopology.ShortestPath(graph, from, to);
    }
}
