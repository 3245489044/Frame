
using System.Collections.Concurrent;

namespace Modules.Registry;


public class StageRegistryBase<TStage, TValue> : RegistryBase<TStage>, IStageRegistry<TStage, TValue>
    where TStage : struct, Enum
{
    private readonly ConcurrentDictionary<TStage, List<TValue>> _store = new();


    protected override bool StoreContainsKey(TStage key) => _store.ContainsKey(key);

    protected override bool StoreTryRemove(TStage key) => _store.TryRemove(key, out _);

    protected override void StoreClear() => _store.Clear();


    protected virtual void OnBeforeRegister(TStage stage, TValue value) { }

    public void Register(TStage stage, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        OnBeforeRegister(stage, value);
        _store.AddOrUpdate(
            stage,
            _ => new List<TValue> { value },
            (_, list) => { lock (list) { list.Add(value); } return list; });
    }

    public void InsertAt(TStage stage, int index, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        OnBeforeRegister(stage, value);
        var list = _store.GetOrAdd(stage, _ => new List<TValue>());
        lock (list)
        {
            index = Math.Clamp(index, 0, list.Count);
            list.Insert(index, value);
        }
    }

    public void Replace(TStage stage, int index, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        OnBeforeRegister(stage, value);
        if (_store.TryGetValue(stage, out var list))
        {
            lock (list)
            {
                if (index >= 0 && index < list.Count)
                    list[index] = value;
            }
        }
    }

    public void ReplaceAll(TStage stage, IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _store[stage] = values.ToList();
    }

    public void Remove(TStage stage, int index)
    {
        if (_store.TryGetValue(stage, out var list))
        {
            lock (list)
            {
                if (index >= 0 && index < list.Count)
                    list.RemoveAt(index);
            }
        }
    }

    public void ClearStage(TStage stage)
    {
        OnBeforeUnregister(stage);
        _store.TryRemove(stage, out _);
    }

    public override void Clear()
    {
        OnBeforeClear();
        _store.Clear();
    }

    public IReadOnlyList<TValue> GetValues(TStage stage)
    {
        return _store.TryGetValue(stage, out var list)
            ? list.ToList().AsReadOnly()
            : Array.Empty<TValue>();
    }

    public IReadOnlyDictionary<TStage, IReadOnlyList<TValue>> GetAllValues()
    {
        return _store.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<TValue>)kvp.Value.ToList().AsReadOnly());
    }

    public int GetCount(TStage stage)
    {
        return _store.TryGetValue(stage, out var list) ? list.Count : 0;
    }

    public IEnumerable<TStage> GetStages() => _store.Keys;

    public int TotalCount
    {
        get
        {
            return _store.Values.Sum(list => list.Count);
        }
    }
}


public class GraphRegistryBase<TNodeId, TNode, TEdge> : RegistryBase<TNodeId>, IGraphRegistry<TNodeId, TNode, TEdge>
    where TNodeId : notnull
{
    private readonly ConcurrentDictionary<TNodeId, TNode> _nodes = new();
    private readonly ConcurrentDictionary<TNodeId, List<(TNodeId To, TEdge Edge)>> _outgoing = new();
    private readonly ConcurrentDictionary<TNodeId, List<(TNodeId From, TEdge Edge)>> _incoming = new();
    private readonly object _edgeLock = new();


    protected override bool StoreContainsKey(TNodeId key) => _nodes.ContainsKey(key);

    protected override bool StoreTryRemove(TNodeId key)
    {
        lock (_edgeLock)
        {
            foreach (var from in _outgoing.Keys)
            {
                if (_outgoing.TryGetValue(from, out var edges))
                {
                    edges.RemoveAll(e => e.To.Equals(key));
                    if (edges.Count == 0) _outgoing.TryRemove(from, out _);
                }
            }
            foreach (var to in _incoming.Keys)
            {
                if (_incoming.TryGetValue(to, out var edges))
                {
                    edges.RemoveAll(e => e.From.Equals(key));
                    if (edges.Count == 0) _incoming.TryRemove(to, out _);
                }
            }
        }
        _outgoing.TryRemove(key, out _);
        _incoming.TryRemove(key, out _);
        return _nodes.TryRemove(key, out _);
    }

    protected override void StoreClear()
    {
        _nodes.Clear();
        _outgoing.Clear();
        _incoming.Clear();
    }

    public void RegisterNode(TNodeId id, TNode node)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(node);
        _nodes[id] = node;
    }

    public void RegisterEdge(TNodeId from, TNodeId to, TEdge edge)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(edge);

        lock (_edgeLock)
        {
            _outgoing.AddOrUpdate(from,
                _ => new List<(TNodeId, TEdge)> { (to, edge) },
                (_, list) => { list.Add((to, edge)); return list; });

            _incoming.AddOrUpdate(to,
                _ => new List<(TNodeId, TEdge)> { (from, edge) },
                (_, list) => { list.Add((from, edge)); return list; });
        }
    }

    public TNode? GetNode(TNodeId id)
        => _nodes.TryGetValue(id, out var node) ? node : default;

    public IEnumerable<TEdge> GetOutgoingEdges(TNodeId nodeId)
        => _outgoing.TryGetValue(nodeId, out var edges)
            ? edges.Select(e => e.Edge) : Enumerable.Empty<TEdge>();

    public IEnumerable<TEdge> GetIncomingEdges(TNodeId nodeId)
        => _incoming.TryGetValue(nodeId, out var edges)
            ? edges.Select(e => e.Edge) : Enumerable.Empty<TEdge>();

    public void UnregisterNode(TNodeId id)
    {
        Unregister(id);
    }

    public void UnregisterEdge(TNodeId from, TNodeId to)
    {
        lock (_edgeLock)
        {
            if (_outgoing.TryGetValue(from, out var outEdges))
            {
                outEdges.RemoveAll(e => e.To.Equals(to));
                if (outEdges.Count == 0) _outgoing.TryRemove(from, out _);
            }
            if (_incoming.TryGetValue(to, out var inEdges))
            {
                inEdges.RemoveAll(e => e.From.Equals(from));
                if (inEdges.Count == 0) _incoming.TryRemove(to, out _);
            }
        }
    }

    public IEnumerable<TNode> GetAllNodes() => _nodes.Values;

    public IEnumerable<TEdge> GetAllEdges()
        => _outgoing.Values.SelectMany(list => list.Select(e => e.Edge));

    public IEnumerable<TNodeId> TopologicalSort(TNodeId startNodeId)
    {
        var gray = new HashSet<TNodeId>();
        var black = new HashSet<TNodeId>();
        var result = new List<TNodeId>();
        DfsWithCycleDetection(startNodeId, gray, black, result);
        return result;
    }

    public IEnumerable<TNodeId> TopologicalSortAll()
    {
        var gray = new HashSet<TNodeId>();
        var black = new HashSet<TNodeId>();
        var result = new List<TNodeId>();

        foreach (var nodeId in _nodes.Keys)
        {
            if (!black.Contains(nodeId))
                DfsWithCycleDetection(nodeId, gray, black, result);
        }

        return result;
    }

    private void DfsWithCycleDetection(
        TNodeId nodeId,
        HashSet<TNodeId> gray,
        HashSet<TNodeId> black,
        List<TNodeId> result)
    {
        if (black.Contains(nodeId)) return;
        if (gray.Contains(nodeId))
            throw new InvalidOperationException(
                $"Cycle detected in graph at node '{nodeId}'. Topological sort cannot be completed.");

        gray.Add(nodeId);
        if (_outgoing.TryGetValue(nodeId, out var edges))
        {
            foreach (var (to, _) in edges)
                DfsWithCycleDetection(to, gray, black, result);
        }
        gray.Remove(nodeId);
        black.Add(nodeId);
        result.Add(nodeId);
    }
}
