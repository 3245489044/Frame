
using System.Diagnostics.CodeAnalysis;

namespace Modules.Registry;


public interface IRegistry
{
    void Clear();
}


public interface ISetRegistry<TKey> : IRegistry
    where TKey : notnull
{
    void Register(TKey key);

    bool Contains(TKey key);

    IEnumerable<TKey> GetKeys();

    void Unregister(TKey key);

    int Count { get; }
}


public interface IMapRegistry<TKey, TValue> : IRegistry
    where TKey : notnull
{
    void Register(TKey key, TValue value);

    bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value);

    bool Contains(TKey key);

    IEnumerable<TKey> GetKeys();

    void Unregister(TKey key);

    int Count { get; }
}


public interface IFactoryRegistry<TKey, TValue> : IRegistry
    where TKey : notnull
{
    void Register(TKey name, Func<IServiceProvider?, TValue> factory);

    TValue? Resolve(TKey name, IServiceProvider? serviceProvider = null);

    bool Contains(TKey key);

    IEnumerable<TKey> GetKeys();

    int Count { get; }
}


public interface IStageRegistry<TStage, TValue> : IRegistry
    where TStage : struct, Enum
{
    void Register(TStage stage, TValue value);

    void InsertAt(TStage stage, int index, TValue value);

    void Replace(TStage stage, int index, TValue value);

    void ReplaceAll(TStage stage, IEnumerable<TValue> values);

    void Remove(TStage stage, int index);

    void ClearStage(TStage stage);

    IReadOnlyList<TValue> GetValues(TStage stage);

    IReadOnlyDictionary<TStage, IReadOnlyList<TValue>> GetAllValues();

    int GetCount(TStage stage);

    IEnumerable<TStage> GetStages();

    int TotalCount { get; }
}


public interface IGraphRegistry<TNodeId, TNode, TEdge> : IRegistry
    where TNodeId : notnull
{
    void RegisterNode(TNodeId id, TNode node);

    void RegisterEdge(TNodeId from, TNodeId to, TEdge edge);

    TNode? GetNode(TNodeId id);

    IEnumerable<TEdge> GetOutgoingEdges(TNodeId nodeId);

    IEnumerable<TEdge> GetIncomingEdges(TNodeId nodeId);

    void UnregisterNode(TNodeId id);

    void UnregisterEdge(TNodeId from, TNodeId to);

    IEnumerable<TNode> GetAllNodes();

    IEnumerable<TEdge> GetAllEdges();

    IEnumerable<TNodeId> TopologicalSort(TNodeId startNodeId);

    IEnumerable<TNodeId> TopologicalSortAll();
}


public enum RegistryChangeType
{
    Registered,
    Updated,
    Unregistered,
    Cleared
}

public class RegistryChangedEventArgs<TKey, TValue> : EventArgs
{
    public RegistryChangeType ChangeType { get; init; }

    public TKey Key { get; init; } = default!;

    public TValue? OldValue { get; init; }

    public TValue? NewValue { get; init; }
}

public interface INotifyRegistry<TKey, TValue> : IRegistry
{
    event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemRegistered;

    event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUpdated;

    event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUnregistered;

    event EventHandler? RegistryCleared;
}
