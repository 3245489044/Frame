
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UniversalMiddleware.Extensions;

public static class JsonNodeExtensions
{
    public static bool TryGetValue<T>(this JsonNode node, string propertyName, out T? value)
    {
        value = default;
        if (node is JsonObject obj && obj.TryGetPropertyValue(propertyName, out var child))
        {
            if (child == null)
                return false;

            try
            {
                value = child.GetValue<T>();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        return false;
    }

    public static T? GetValueOrDefault<T>(this JsonNode node, string propertyName, T? defaultValue = default)
    {
        return TryGetValue<T>(node, propertyName, out var value) ? value : defaultValue;
    }

    public static string? GetString(this JsonNode node, string propertyName)
    {
        return GetValueOrDefault<string>(node, propertyName);
    }

    public static int? GetInt(this JsonNode node, string propertyName)
    {
        return GetValueOrDefault<int>(node, propertyName);
    }

    public static bool? GetBool(this JsonNode node, string propertyName)
    {
        return GetValueOrDefault<bool>(node, propertyName);
    }

    public static JsonObject? GetObject(this JsonNode node, string propertyName)
    {
        return TryGetValue<JsonObject>(node, propertyName, out var value) ? value : null;
    }

    public static JsonArray? GetArray(this JsonNode node, string propertyName)
    {
        return TryGetValue<JsonArray>(node, propertyName, out var value) ? value : null;
    }

    public static JsonObject SetProperty(this JsonObject obj, string propertyName, JsonNode? value)
    {
        obj[propertyName] = value;
        return obj;
    }

    public static JsonObject RemoveProperty(this JsonObject obj, string propertyName)
    {
        obj.Remove(propertyName);
        return obj;
    }

    public static string ToPrettyString(this JsonNode node)
    {
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static JsonObject MergeFrom(this JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value != null)
                target[key] = value.DeepClone();
        }
        return target;
    }
}
