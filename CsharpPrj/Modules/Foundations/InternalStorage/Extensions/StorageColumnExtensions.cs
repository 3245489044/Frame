
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Extensions;

public static class StorageColumnExtensions
{

    public static void SetComponentColumn<TComponent>(
        this IECSStorage storage,
        int chunkId,
        int componentTypeId,
        TComponent[] column)
        where TComponent : struct
    {
        storage.SetChunkColumn(chunkId, componentTypeId, column);
    }

    public static TComponent[]? GetComponentColumn<TComponent>(
        this IECSStorage storage,
        int chunkId,
        int componentTypeId)
        where TComponent : struct
    {
        var column = storage.GetChunkColumn(chunkId, componentTypeId);
        return column as TComponent[];
    }

    public static Span<TComponent> GetColumnSpan<TComponent>(
        this IECSStorage storage,
        int chunkId,
        int componentTypeId)
        where TComponent : struct
    {
        if (TryGetColumnSpan(storage, chunkId, componentTypeId, out Span<TComponent> span))
        {
            return span;
        }
        throw new InvalidOperationException(
            $"Chunk {chunkId} 的 Component 类型 {componentTypeId} 列不存在或类型不匹配。");
    }

    public static bool TryGetColumnSpan<TComponent>(
        this IECSStorage storage,
        int chunkId,
        int componentTypeId,
        out Span<TComponent> span)
        where TComponent : struct
    {
        var column = storage.GetChunkColumn(chunkId, componentTypeId);
        if (column is TComponent[] typedColumn)
        {
            span = typedColumn.AsSpan();
            return true;
        }

        span = default;
        return false;
    }


    public static void SetComponentColumns(
        this IECSStorage storage,
        int chunkId,
        IDictionary<int, Array> columns)
    {
        foreach (var kvp in columns)
        {
            storage.SetChunkColumn(chunkId, kvp.Key, kvp.Value);
        }
    }

    public static IDictionary<int, Array> GetComponentColumns(
        this IECSStorage storage,
        int chunkId,
        IEnumerable<int> componentTypeIds)
    {
        var result = new Dictionary<int, Array>();
        foreach (var typeId in componentTypeIds)
        {
            var column = storage.GetChunkColumn(chunkId, typeId);
            if (column != null)
            {
                result[typeId] = column;
            }
        }
        return result;
    }

    public static int GetColumnLength(
        this IECSStorage storage,
        int chunkId,
        int componentTypeId)
    {
        var column = storage.GetChunkColumn(chunkId, componentTypeId);
        return column?.Length ?? 0;
    }

    public static bool HasComponentColumn(
        this IECSStorage storage,
        int chunkId,
        int componentTypeId)
    {
        return storage.GetChunkColumn(chunkId, componentTypeId) != null;
    }
}
