
#nullable enable

namespace GraphFoundation.Storage
{
    public interface IGraphStorage<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {

        bool TryAddNode(TKey key, TNodeData data, string? symbol);

        bool TryRemoveNode(TKey key, out TNodeData? oldData, out string? oldSymbol);

        bool TryGetNode(TKey key, out TNodeData? data, out string? symbol);

        bool TryUpdateNode(TKey key, TNodeData newData, string? newSymbol,
                        out TNodeData? oldData, out string? oldSymbol);

        bool ContainsNode(TKey key);

        IEnumerable<TKey> GetAllNodeKeys();

        int NodeCount { get; }


        bool TryAddEdge(string edgeId, TKey source, TKey target,
                        bool isDirected, TEdgeData? data, double weight);

        bool TryRemoveEdge(string edgeId, out EdgeSnapshot<TKey, TEdgeData> snapshot);

        bool TryGetEdge(string edgeId, out EdgeSnapshot<TKey, TEdgeData> snapshot);

        bool ContainsEdge(string edgeId);

        IEnumerable<string> GetAllEdgeIds();

        int EdgeCount { get; }


        IEnumerable<EdgeSnapshot<TKey, TEdgeData>> GetOutgoingEdges(TKey source);

        IEnumerable<EdgeSnapshot<TKey, TEdgeData>> GetIncomingEdges(TKey target);

        IEnumerable<TKey> GetNeighbors(TKey key);


        void Clear();

        bool IsInitialized { get; }
    }
}
