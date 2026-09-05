
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Storage;

namespace GraphFoundation.Topology.Adapters
{
    public class CoreGraphReadOnlyAdapter<TKey, TNodeData, TEdgeData> : IReadOnlyGraph<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IGraphStorage<TKey, TNodeData, TEdgeData> _storage;

        public CoreGraphReadOnlyAdapter(IGraphStorage<TKey, TNodeData, TEdgeData> storage)
        {
            _storage = storage;
        }

        public IObservable<GraphEvent> Events
            => throw new NotSupportedException("Events are not supported when using IGraphStorage directly. Use ICoreGraph instead.");

        public TNodeData? GetNodeData(TKey key)
            => _storage.TryGetNode(key, out var data, out _) ? data : null;

        public string? GetNodeSymbol(TKey key)
            => _storage.TryGetNode(key, out _, out var symbol) ? symbol : null;

        public TEdgeData? GetEdgeData(string edgeId)
            => _storage.TryGetEdge(edgeId, out var snapshot) ? snapshot.Data : default;

        public double GetEdgeWeight(string edgeId)
        {
            if (_storage.TryGetEdge(edgeId, out var snapshot))
                return snapshot.Weight;
            throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
        }

        public TKey GetEdgeSource(string edgeId)
        {
            if (_storage.TryGetEdge(edgeId, out var snapshot))
                return snapshot.Source;
            throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
        }

        public TKey GetEdgeTarget(string edgeId)
        {
            if (_storage.TryGetEdge(edgeId, out var snapshot))
                return snapshot.Target;
            throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
        }

        public bool GetEdgeIsDirected(string edgeId)
        {
            if (_storage.TryGetEdge(edgeId, out var snapshot))
                return snapshot.IsDirected;
            throw new KeyNotFoundException($"Edge with id '{edgeId}' not found.");
        }

        public IReadOnlyList<TKey> GetAllNodeKeys()
            => _storage.GetAllNodeKeys().ToList();

        public IReadOnlyList<string> GetAllEdgeIds()
            => _storage.GetAllEdgeIds().ToList();

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetOutgoingEdges(TKey source)
            => _storage.GetOutgoingEdges(source)
                .Select(s => (s.Source, s.Target, s.EdgeId, s.IsDirected, s.Weight, s.Data!))
                .ToList();

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetIncomingEdges(TKey target)
            => _storage.GetIncomingEdges(target)
                .Select(s => (s.Source, s.Target, s.EdgeId, s.IsDirected, s.Weight, s.Data!))
                .ToList();

        public bool ContainsNode(TKey key)
            => _storage.ContainsNode(key);

        public bool ContainsEdge(string edgeId)
            => _storage.ContainsEdge(edgeId);
    }
}
