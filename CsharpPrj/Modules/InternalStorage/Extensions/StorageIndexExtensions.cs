
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Extensions;

public static class StorageIndexExtensions
{
    private const string IndexMetaPrefix = "index_meta:";
    private const string IndexDataPrefix = "index_data:";
    private const string IndexSetPrefix = "index_set:";


    public static void RegisterIndex(
        this IStorageCell cell,
        string indexName,
        string entityType,
        bool isUnique,
        StorageLayer layer = StorageLayer.Session)
    {
        var metaKey = $"{IndexMetaPrefix}{indexName}";
        cell.Set(metaKey, new IndexMetaData
        {
            Name = indexName,
            EntityType = entityType,
            IsUnique = isUnique
        }, layer);
    }

    public static void AddUniqueIndex<TKey, TValue>(
        this IStorageCell cell,
        string indexName,
        TKey key,
        TValue value,
        StorageLayer layer)
        where TKey : notnull
        where TValue : notnull
    {
        var meta = cell.GetIndexMeta(indexName, layer);
        if (meta == null)
        {
            cell.RegisterIndex(indexName, typeof(TValue).FullName ?? "unknown", true, layer);
        }

        var indexKey = $"{IndexDataPrefix}{indexName}:{key}";

        if (cell.Contains(indexKey, layer))
        {
            throw new InvalidOperationException(
                $"唯一索引 '{indexName}' 的键 '{key}' 已存在。");
        }

        cell.Set(indexKey, value, layer);
    }

    public static IndexMetaData? GetIndexMeta(
        this IStorageCell cell,
        string indexName,
        StorageLayer layer = StorageLayer.Session)
    {
        var metaKey = $"{IndexMetaPrefix}{indexName}";
        return cell.Get<IndexMetaData>(metaKey, layer);
    }


    public static void IndexAdd(
        this IStorageCell cell,
        string indexName,
        string indexKey,
        string entityId,
        StorageLayer layer = StorageLayer.Session)
    {
        var meta = cell.GetIndexMeta(indexName, layer);
        if (meta == null)
            throw new InvalidOperationException($"索引 '{indexName}' 未注册。请先调用 RegisterIndex。");

        if (meta.IsUnique)
        {
            var dataKey = $"{IndexDataPrefix}{indexName}:{indexKey}";
            cell.Set(dataKey, entityId, layer);
        }
        else
        {
            var setKey = $"{IndexSetPrefix}{indexName}:{indexKey}";
            cell.SetAdd(setKey, entityId, layer);
        }
    }

    public static void IndexRemove(
        this IStorageCell cell,
        string indexName,
        string indexKey,
        string? entityId = null,
        StorageLayer layer = StorageLayer.Session)
    {
        var meta = cell.GetIndexMeta(indexName, layer);
        if (meta == null) return;

        if (meta.IsUnique)
        {
            var dataKey = $"{IndexDataPrefix}{indexName}:{indexKey}";
            cell.Remove(dataKey, layer);
        }
        else
        {
            if (!string.IsNullOrEmpty(entityId))
            {
                var setKey = $"{IndexSetPrefix}{indexName}:{indexKey}";
                cell.SetRemove(setKey, entityId, layer);
            }
        }
    }

    public static void IndexUpdate(
        this IStorageCell cell,
        string indexName,
        string oldKey,
        string newKey,
        string entityId,
        StorageLayer layer = StorageLayer.Session)
    {
        if (oldKey != newKey)
        {
            IndexRemove(cell, indexName, oldKey, entityId, layer);
            IndexAdd(cell, indexName, newKey, entityId, layer);
        }
    }


    public static string? FindByIndex(
        this IStorageCell cell,
        string indexName,
        string key,
        StorageLayer layer = StorageLayer.Session)
    {
        var meta = cell.GetIndexMeta(indexName, layer);
        if (meta == null) return null;

        if (meta.IsUnique)
        {
            var dataKey = $"{IndexDataPrefix}{indexName}:{key}";
            return cell.Get<string>(dataKey, layer);
        }
        else
        {
            var setKey = $"{IndexSetPrefix}{indexName}:{key}";
            return cell.SetFirst<string>(setKey, layer);
        }
    }

    public static IEnumerable<string> FindAllByIndex(
        this IStorageCell cell,
        string indexName,
        string key,
        StorageLayer layer = StorageLayer.Session)
    {
        var meta = cell.GetIndexMeta(indexName, layer);
        if (meta == null) yield break;

        if (meta.IsUnique)
        {
            var dataKey = $"{IndexDataPrefix}{indexName}:{key}";
            var id = cell.Get<string>(dataKey, layer);
            if (!string.IsNullOrEmpty(id))
                yield return id;
        }
        else
        {
            var setKey = $"{IndexSetPrefix}{indexName}:{key}";
            foreach (var id in cell.SetGetAll<string>(setKey, layer))
            {
                yield return id;
            }
        }
    }

    public static bool IndexContains(
        this IStorageCell cell,
        string indexName,
        string key,
        StorageLayer layer = StorageLayer.Session)
    {
        var meta = cell.GetIndexMeta(indexName, layer);
        if (meta == null) return false;

        if (meta.IsUnique)
        {
            var dataKey = $"{IndexDataPrefix}{indexName}:{key}";
            return cell.Contains(dataKey, layer);
        }
        else
        {
            var setKey = $"{IndexSetPrefix}{indexName}:{key}";
            return cell.Contains(setKey, layer);
        }
    }
}

public class IndexMetaData
{
    public string Name { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public bool IsUnique { get; set; }
}
