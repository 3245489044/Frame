namespace Rge.Unified.Models;

public sealed class ExecutionGraph
{
    public string GraphId { get; init; } = string.Empty;

    public string? EntryEntity { get; set; }

    public Dictionary<string, Entity> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DependencyEdge> Edges => _edges;

    private readonly List<DependencyEdge> _edges = new();
    private Dictionary<string, List<string>>? _predecessorIndex;
    private Dictionary<string, List<string>>? _successorIndex;

    public Dictionary<string, string> OutputMapping { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object? RootInput { get; set; }

    public ExecutionGraph? Parent { get; internal set; }

    public string? HostEntityUid { get; internal set; }

    public ExecutionGraph? ParentGraphOf(Entity entity)
    {
        if (entity is null)
            return null;
        return ReferenceEquals(entity.SubGraph, this) ? Parent : null;
    }

    public ExecutionGraph SetParent(ExecutionGraph parent, string hostEntityUid)
    {
        Parent = parent;
        HostEntityUid = hostEntityUid;
        return this;
    }

    public ExecutionGraph AddEntity(Entity entity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity.Uid, nameof(entity.Uid));
        Entities[entity.Uid] = entity;
        return this;
    }

    public ExecutionGraph AddEdge(DependencyEdge edge) => AddEdge(edge.From, edge.To, edge.TargetParam, edge.SourceKey);

    public ExecutionGraph AddEdge(string from, string to, string? targetParam = null, string? sourceKey = null)
    {
        _edges.Add(new DependencyEdge { From = from, To = to, TargetParam = targetParam, SourceKey = sourceKey });
        InvalidateEdgeIndex();
        return this;
    }

    public bool RemoveEdge(DependencyEdge edge)
    {
        var removed = _edges.Remove(edge);
        if (removed)
            InvalidateEdgeIndex();
        return removed;
    }

    public ExecutionGraph WithOutput(string entityKey, string exposedKey)
    {
        OutputMapping[entityKey] = exposedKey;
        return this;
    }

    public Entity? Find(string uid) =>
        Entities.TryGetValue(uid, out var e) ? e : null;

    public IEnumerable<Entity> GetPredecessors(string uid)
    {
        _predecessorIndex ??= BuildEdgeIndex(reverse: true);
        return ResolveIndexedEntities(_predecessorIndex, uid);
    }

    public IEnumerable<Entity> GetSuccessors(string uid)
    {
        _successorIndex ??= BuildEdgeIndex(reverse: false);
        return ResolveIndexedEntities(_successorIndex, uid);
    }

    private Dictionary<string, List<string>> BuildEdgeIndex(bool reverse)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in _edges)
        {
            var key = reverse ? edge.To : edge.From;
            var value = reverse ? edge.From : edge.To;
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<string>();
                index[key] = list;
            }
            list.Add(value);
        }
        return index;
    }

    private IEnumerable<Entity> ResolveIndexedEntities(
        Dictionary<string, List<string>> index, string uid)
    {
        if (index.TryGetValue(uid, out var uids))
        {
            foreach (var id in uids)
                if (Entities.TryGetValue(id, out var entity))
                    yield return entity;
        }
    }

    private void InvalidateEdgeIndex()
    {
        _predecessorIndex = null;
        _successorIndex = null;
    }

    public int CountAllEntities()
    {
        int count = 0;
        CountRecursive(this, ref count);
        return count;
    }

    private static void CountRecursive(ExecutionGraph graph, ref int count)
    {
        count += graph.Entities.Count;
        foreach (var entity in graph.Entities.Values)
        {
            if (entity.SubGraph != null)
                CountRecursive(entity.SubGraph, ref count);
        }
    }
}