
#nullable enable

namespace GraphFoundation.Abstractions
{
    public abstract record GraphEvent();

    public record NodeAddedEvent<TKey>(TKey Key, object? Data, string? Symbol) : GraphEvent;

    public record NodeRemovedEvent<TKey>(TKey Key, object? OldData, string? OldSymbol) : GraphEvent;

    public record NodeUpdatedEvent<TKey>(TKey Key, object? OldData, object? NewData, string? OldSymbol, string? NewSymbol) : GraphEvent;

    public record EdgeAddedEvent<TKey>(string EdgeId, TKey Source, TKey Target, bool IsDirected, double Weight, object? Data) : GraphEvent;

    public record EdgeRemovedEvent<TKey>(string EdgeId, TKey Source, TKey Target) : GraphEvent;

    public record EdgeUpdatedEvent<TKey>(string EdgeId, TKey Source, TKey Target, bool IsDirected, double Weight, object? OldData, object? NewData) : GraphEvent;

    public interface IReadOnlyGraph<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        TNodeData? GetNodeData(TKey key);

        string? GetNodeSymbol(TKey key);

        TEdgeData? GetEdgeData(string edgeId);

        double GetEdgeWeight(string edgeId);

        TKey GetEdgeSource(string edgeId);

        TKey GetEdgeTarget(string edgeId);

        bool GetEdgeIsDirected(string edgeId);

        IReadOnlyList<TKey> GetAllNodeKeys();

        IReadOnlyList<string> GetAllEdgeIds();

        IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetOutgoingEdges(TKey source);

        IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetIncomingEdges(TKey target);

        bool ContainsNode(TKey key);

        bool ContainsEdge(string edgeId);

        IObservable<GraphEvent> Events { get; }
    }

    public interface IGraph<TKey, TNodeData, TEdgeData> : IReadOnlyGraph<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        ITransaction BeginTransaction();

        void AddNode(TKey key, TNodeData data, string? symbol = null);

        void RemoveNode(TKey key);

        void AddEdge(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0);

        void RemoveEdge(string edgeId);

        void UpdateNode(TKey key, TNodeData newData, string? newSymbol = null);

        IReadOnlyList<TKey> GetNeighbors(TKey key);
    }

    public interface ICoreGraph<TKey, TNodeData, TEdgeData> : IGraph<TKey, TNodeData, TEdgeData>, IDisposable
        where TKey : notnull
        where TNodeData : class
    {
    }
}
