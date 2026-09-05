
#nullable enable

using System.Collections.Concurrent;

namespace GraphFoundation.Storage
{
    internal class NodeEntry<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        public TKey Key;
        public TNodeData Data;
        public string? Symbol;

        public NodeEntry(TKey key, TNodeData data, string? symbol = null)
        {
            Key = key;
            Data = data;
            Symbol = symbol;
        }
    }

    internal class EdgeEntry<TKey, TEdgeData>
        where TKey : notnull
    {
        public string Id;
        public TKey Source;
        public TKey Target;
        public bool IsDirected;
        public double Weight;
        public TEdgeData? Data;

        public EdgeEntry(string id, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0)
        {
            Id = id;
            Source = source;
            Target = target;
            IsDirected = isDirected;
            Data = data;
            Weight = weight;
        }
    }

    public class InMemoryGraphStorage<TKey, TNodeData, TEdgeData> : IGraphStorage<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ConcurrentDictionary<TKey, NodeEntry<TKey, TNodeData>> _nodes = new();
        private readonly ConcurrentDictionary<string, EdgeEntry<TKey, TEdgeData>> _edges = new();

        private readonly ConcurrentDictionary<TKey, ConcurrentDictionary<string, byte>> _outgoingEdges = new();
        private readonly ConcurrentDictionary<TKey, ConcurrentDictionary<string, byte>> _incomingEdges = new();


        public bool TryAddNode(TKey key, TNodeData data, string? symbol)
        {
            return _nodes.TryAdd(key, new NodeEntry<TKey, TNodeData>(key, data, symbol));
        }

        public bool TryRemoveNode(TKey key, out TNodeData? oldData, out string? oldSymbol)
        {
            if (_nodes.TryRemove(key, out var entry))
            {
                oldData = entry.Data;
                oldSymbol = entry.Symbol;

                if (_outgoingEdges.TryRemove(key, out var outEdgeIds))
                {
                    foreach (var edgeId in outEdgeIds.Keys)
                    {
                        if (_edges.TryRemove(edgeId, out var removedEdge))
                        {
                            if (_incomingEdges.TryGetValue(removedEdge.Target, out var targetInSet))
                            {
                                targetInSet.TryRemove(edgeId, out _);
                                if (targetInSet.IsEmpty)
                                    _incomingEdges.TryRemove(removedEdge.Target, out _);
                            }
                        }
                    }
                }

                if (_incomingEdges.TryRemove(key, out var inEdgeIds))
                {
                    foreach (var edgeId in inEdgeIds.Keys)
                    {
                        if (_edges.TryRemove(edgeId, out var removedEdge))
                        {
                            if (_outgoingEdges.TryGetValue(removedEdge.Source, out var sourceOutSet))
                            {
                                sourceOutSet.TryRemove(edgeId, out _);
                                if (sourceOutSet.IsEmpty)
                                    _outgoingEdges.TryRemove(removedEdge.Source, out _);
                            }
                        }
                    }
                }

                return true;
            }

            oldData = null;
            oldSymbol = null;
            return false;
        }


        public bool TryGetNode(TKey key, out TNodeData? data, out string? symbol)
        {
            if (_nodes.TryGetValue(key, out var entry))
            {
                data = entry.Data;
                symbol = entry.Symbol;
                return true;
            }

            data = null;
            symbol = null;
            return false;
        }

        public bool TryUpdateNode(TKey key, TNodeData newData, string? newSymbol,
                                  out TNodeData? oldData, out string? oldSymbol)
        {
            if (_nodes.TryGetValue(key, out var entry))
            {
                oldData = entry.Data;
                oldSymbol = entry.Symbol;
                entry.Data = newData;
                entry.Symbol = newSymbol;
                return true;
            }

            oldData = null;
            oldSymbol = null;
            return false;
        }


        public bool ContainsNode(TKey key) => _nodes.ContainsKey(key);

        public IEnumerable<TKey> GetAllNodeKeys() => _nodes.Keys;

        public int NodeCount => _nodes.Count;


        public bool TryAddEdge(string edgeId, TKey source, TKey target,
                               bool isDirected, TEdgeData? data, double weight)
        {
            var entry = new EdgeEntry<TKey, TEdgeData>(edgeId, source, target, isDirected, data, weight);
            if (!_edges.TryAdd(edgeId, entry))
                return false;

            _outgoingEdges.GetOrAdd(source, _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(edgeId, 0);

            _incomingEdges.GetOrAdd(target, _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(edgeId, 0);

            return true;
        }

        public bool TryRemoveEdge(string edgeId, out EdgeSnapshot<TKey, TEdgeData> snapshot)
        {
            if (_edges.TryRemove(edgeId, out var entry))
            {
                snapshot = new EdgeSnapshot<TKey, TEdgeData>(
                    entry.Id, entry.Source, entry.Target,
                    entry.IsDirected, entry.Weight, entry.Data);

                if (_outgoingEdges.TryGetValue(entry.Source, out var outSet))
                {
                    outSet.TryRemove(edgeId, out _);
                    if (outSet.IsEmpty)
                        _outgoingEdges.TryRemove(entry.Source, out _);
                }

                if (_incomingEdges.TryGetValue(entry.Target, out var inSet))
                {
                    inSet.TryRemove(edgeId, out _);
                    if (inSet.IsEmpty)
                        _incomingEdges.TryRemove(entry.Target, out _);
                }

                return true;
            }

            snapshot = default;
            return false;
        }

        public bool TryGetEdge(string edgeId, out EdgeSnapshot<TKey, TEdgeData> snapshot)
        {
            if (_edges.TryGetValue(edgeId, out var entry))
            {
                snapshot = new EdgeSnapshot<TKey, TEdgeData>(
                    entry.Id, entry.Source, entry.Target,
                    entry.IsDirected, entry.Weight, entry.Data);
                return true;
            }

            snapshot = default;
            return false;
        }

        public bool ContainsEdge(string edgeId) => _edges.ContainsKey(edgeId);

        public IEnumerable<string> GetAllEdgeIds() => _edges.Keys;

        public int EdgeCount => _edges.Count;


        public IEnumerable<EdgeSnapshot<TKey, TEdgeData>> GetOutgoingEdges(TKey source)
        {
            if (_outgoingEdges.TryGetValue(source, out var edgeIds))
            {
                foreach (var edgeId in edgeIds.Keys)
                {
                    if (_edges.TryGetValue(edgeId, out var entry))
                    {
                        yield return new EdgeSnapshot<TKey, TEdgeData>(
                            entry.Id, entry.Source, entry.Target,
                            entry.IsDirected, entry.Weight, entry.Data);
                    }
                }
            }
        }

        public IEnumerable<EdgeSnapshot<TKey, TEdgeData>> GetIncomingEdges(TKey target)
        {
            if (_incomingEdges.TryGetValue(target, out var edgeIds))
            {
                foreach (var edgeId in edgeIds.Keys)
                {
                    if (_edges.TryGetValue(edgeId, out var entry))
                    {
                        yield return new EdgeSnapshot<TKey, TEdgeData>(
                            entry.Id, entry.Source, entry.Target,
                            entry.IsDirected, entry.Weight, entry.Data);
                    }
                }
            }
        }

        public IEnumerable<TKey> GetNeighbors(TKey key)
        {
            var visited = new HashSet<TKey>();

            if (_outgoingEdges.TryGetValue(key, out var outIds))
            {
                foreach (var edgeId in outIds.Keys)
                {
                    if (_edges.TryGetValue(edgeId, out var entry))
                        visited.Add(entry.Target);
                }
            }

            if (_incomingEdges.TryGetValue(key, out var inIds))
            {
                foreach (var edgeId in inIds.Keys)
                {
                    if (_edges.TryGetValue(edgeId, out var entry))
                        visited.Add(entry.Source);
                }
            }

            return visited;
        }


        public void Clear()
        {
            _nodes.Clear();
            _edges.Clear();
            _outgoingEdges.Clear();
            _incomingEdges.Clear();
        }

        public bool IsInitialized => true;
    }
}
