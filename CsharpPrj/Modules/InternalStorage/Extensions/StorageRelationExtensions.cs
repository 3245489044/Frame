
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Extensions;

public static class StorageRelationExtensions
{
    private const string RelationPrefix = "relations:";
    private const string ReverseRelationPrefix = "relations_reverse:";


    public static void AddRelation(
        this IStorageCell cell,
        string from,
        string to,
        string relationType,
        StorageLayer layer = StorageLayer.Session)
    {
        var forwardKey = $"{RelationPrefix}{relationType}:{from}";
        var reverseKey = $"{ReverseRelationPrefix}{relationType}:{to}";

        cell.SetAdd(forwardKey, to, layer);
        cell.SetAdd(reverseKey, from, layer);
    }

    public static void RemoveRelation(
        this IStorageCell cell,
        string from,
        string to,
        string relationType,
        StorageLayer layer = StorageLayer.Session)
    {
        var forwardKey = $"{RelationPrefix}{relationType}:{from}";
        var reverseKey = $"{ReverseRelationPrefix}{relationType}:{to}";

        cell.SetRemove(forwardKey, to, layer);
        cell.SetRemove(reverseKey, from, layer);
    }

    public static bool HasRelation(
        this IStorageCell cell,
        string from,
        string to,
        string relationType,
        StorageLayer layer = StorageLayer.Session)
    {
        var forwardKey = $"{RelationPrefix}{relationType}:{from}";
        return cell.SetContains(forwardKey, to, layer);
    }


    public static IEnumerable<string> GetRelated(
        this IStorageCell cell,
        string from,
        string relationType,
        StorageLayer layer = StorageLayer.Session)
    {
        var forwardKey = $"{RelationPrefix}{relationType}:{from}";
        return cell.SetGetAll<string>(forwardKey, layer);
    }


    public static IEnumerable<string> GetRelatedTo(
        this IStorageCell cell,
        string to,
        string relationType,
        StorageLayer layer = StorageLayer.Session)
    {
        var reverseKey = $"{ReverseRelationPrefix}{relationType}:{to}";
        return cell.SetGetAll<string>(reverseKey, layer);
    }


    public static IReadOnlyDictionary<string, IEnumerable<string>> GetAllRelations(
        this IStorageCell cell,
        string from,
        StorageLayer layer = StorageLayer.Session)
    {
        var result = new Dictionary<string, IEnumerable<string>>();
        var prefix = $"{RelationPrefix}";
        var fromSuffix = $":{from}";

        foreach (var key in cell.GetKeys(layer))
        {
            if (key.StartsWith(prefix) && key.EndsWith(fromSuffix))
            {
                var typePart = key[prefix.Length..^fromSuffix.Length];
                if (!string.IsNullOrEmpty(typePart))
                {
                    var values = cell.SetGetAll<string>(key, layer).ToList();
                    if (values.Count > 0)
                    {
                        result[typePart] = values;
                    }
                }
            }
        }

        return result;
    }

    public static void RemoveAllRelations(
        this IStorageCell cell,
        string entity,
        StorageLayer layer = StorageLayer.Session)
    {
        var relations = cell.GetAllRelations(entity, layer);
        var snapshot = relations.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());

        foreach (var kvp in snapshot)
        {
            foreach (var target in kvp.Value)
            {
                cell.RemoveRelation(entity, target, kvp.Key, layer);
            }
        }

        var reversePrefix = $"{ReverseRelationPrefix}";
        var entitySuffix = $":{entity}";

        foreach (var key in cell.GetKeys(layer).ToList())
        {
            if (key.StartsWith(reversePrefix) && key.EndsWith(entitySuffix))
            {
                cell.Remove(key, layer);
            }
        }
    }
}
