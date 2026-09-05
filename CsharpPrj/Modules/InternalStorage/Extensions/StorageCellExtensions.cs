
using TConfig.ContextFrame.Abstractions;
using Modules.InternalStorage.Core;

namespace Modules.InternalStorage.Extensions;

public static class StorageCellExtensions
{

    public static IStorageCell CreateChildCell(this IStorageCell parent, string? childName = null)
    {
        var child = new StorageCell(childName ?? $"{parent.Name}.Child", parent);
        return child;
    }


    public static void SetConfig<T>(this IStorageCell cell, string key, T value) where T : notnull
        => cell.Set(key, value, StorageLayer.Config);

    public static void SetSession<T>(this IStorageCell cell, string key, T value) where T : notnull
        => cell.Set(key, value, StorageLayer.Session);

    public static void SetTransient<T>(this IStorageCell cell, string key, T value) where T : notnull
        => cell.Set(key, value, StorageLayer.Transient);

    public static T? GetConfig<T>(this IStorageCell cell, string key) where T : notnull
        => cell.Get<T>(key, StorageLayer.Config);

    public static T? GetSession<T>(this IStorageCell cell, string key) where T : notnull
        => cell.Get<T>(key, StorageLayer.Session);

    public static T? GetTransient<T>(this IStorageCell cell, string key) where T : notnull
        => cell.Get<T>(key, StorageLayer.Transient);


    public static void SetRange(this IStorageCell cell, IDictionary<string, object?> data, StorageLayer layer = StorageLayer.Session)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        foreach (var kvp in data)
        {
            cell.SetObject(kvp.Key, kvp.Value, layer);
        }
    }

    public static IDictionary<string, object?> CollectAll(this IStorageCell cell, StorageLayer layer)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in cell.GetEntries(layer))
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    public static IDictionary<StorageLayer, IDictionary<string, object?>> CollectAllLayers(this IStorageCell cell)
    {
        return new Dictionary<StorageLayer, IDictionary<string, object?>>
        {
            [StorageLayer.Config] = CollectAll(cell, StorageLayer.Config),
            [StorageLayer.Session] = CollectAll(cell, StorageLayer.Session),
            [StorageLayer.Transient] = CollectAll(cell, StorageLayer.Transient)
        };
    }
}
