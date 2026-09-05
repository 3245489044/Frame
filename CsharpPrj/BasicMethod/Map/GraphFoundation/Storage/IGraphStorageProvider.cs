
#nullable enable

namespace GraphFoundation.Storage
{
    public interface IGraphStorageProvider<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        IGraphStorage<TKey, TNodeData, TEdgeData> Storage { get; }
    }
}
