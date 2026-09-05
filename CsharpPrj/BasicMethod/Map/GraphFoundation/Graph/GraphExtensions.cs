
#nullable enable

using System.Reactive.Linq;
using GraphFoundation.Abstractions;

namespace GraphFoundation.Core
{
    public static class GraphExtensions
    {
        public static IObservable<NodeAddedEvent<TKey>> NodeAdded<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => Observable.OfType<NodeAddedEvent<TKey>>(graph.Events);

        public static IObservable<NodeRemovedEvent<TKey>> NodeRemoved<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => Observable.OfType<NodeRemovedEvent<TKey>>(graph.Events);

        public static IObservable<EdgeAddedEvent<TKey>> EdgeAdded<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => Observable.OfType<EdgeAddedEvent<TKey>>(graph.Events);

        public static IObservable<EdgeRemovedEvent<TKey>> EdgeRemoved<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => Observable.OfType<EdgeRemovedEvent<TKey>>(graph.Events);

        public static int GetOutDegree<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, TKey key)
            where TKey : notnull
            where TNodeData : class
            => graph.GetOutgoingEdges(key).Count;

        public static int GetInDegree<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, TKey key)
            where TKey : notnull
            where TNodeData : class
            => graph.GetIncomingEdges(key).Count;

        public static int GetDegree<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph, TKey key)
            where TKey : notnull
            where TNodeData : class
            => graph.GetOutDegree(key) + graph.GetInDegree(key);

        public static bool IsEmpty<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => graph.GetAllNodeKeys().Count == 0;

        public static int NodeCount<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => graph.GetAllNodeKeys().Count;

        public static int EdgeCount<TKey, TNodeData, TEdgeData>(
            this IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => graph.GetAllEdgeIds().Count;
    }
}
