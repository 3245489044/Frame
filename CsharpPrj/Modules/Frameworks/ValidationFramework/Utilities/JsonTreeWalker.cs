
using System.Text.Json;

namespace ValidationFramework.Utilities;

public static class JsonTreeWalker
{
    public static IReadOnlyList<(string Name, JsonElement Value, string Path)> FindProperties(
        JsonElement root,
        string propertyName,
        int? maxDepth = null,
        string rootPath = "$")
    {
        var results = new List<(string, JsonElement, string)>();
        Walk(root, rootPath, 0);
        return results;

        void Walk(JsonElement element, string path, int depth)
        {
            if (maxDepth.HasValue && depth > maxDepth.Value) return;

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var currentPath = $"{path}.{property.Name}";
                        if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                        {
                            results.Add((property.Name, property.Value, currentPath));
                        }
                        Walk(property.Value, currentPath, depth + 1);
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[{index}]", depth + 1);
                        index++;
                    }
                    break;
            }
        }
    }

    public static string? GetStringOrNull(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
