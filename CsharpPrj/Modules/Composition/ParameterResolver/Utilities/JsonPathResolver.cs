
using System.Text.Json;

namespace ParameterResolver.Utilities;

public static class JsonPathResolver
{
    public static IEnumerable<JsonElement> Resolve(JsonElement root, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = new[] { root };

        foreach (var segment in segments)
        {
            if (segment.EndsWith("[]"))
            {
                var propName = segment[..^2];
                current = current
                    .SelectMany(e => e.TryGetProperty(propName, out var arr) && arr.ValueKind == JsonValueKind.Array
                        ? arr.EnumerateArray()
                        : Enumerable.Empty<JsonElement>())
                    .ToArray();
            }
            else
            {
                current = current
                    .Select(e => e.TryGetProperty(segment, out var prop) ? prop : default)
                    .Where(p => p.ValueKind != JsonValueKind.Undefined)
                    .ToArray();
            }

            if (current.Length == 0)
                break;
        }

        return current;
    }

    public static IEnumerable<string> ResolveStrings(JsonElement root, string path)
    {
        return Resolve(root, path)
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrEmpty(s));
    }

    public static JsonElement? ResolveFirst(JsonElement root, string path)
    {
        return Resolve(root, path).FirstOrDefault();
    }
}
