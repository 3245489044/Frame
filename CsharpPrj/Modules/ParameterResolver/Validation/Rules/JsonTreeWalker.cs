
using System.Text.Json;

namespace ParameterResolver.Validation.Rules;

public static class JsonTreeWalker
{
    public static void Walk(
        JsonElement element,
        string currentPath,
        Func<JsonElement, string, bool> visitor,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var shouldRecurse = visitor(element, currentPath);
                if (shouldRecurse)
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value, $"{currentPath}.{property.Name}", visitor, cancellationToken);
                    }
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{currentPath}[{index}]", visitor, cancellationToken);
                    index++;
                }
                break;
        }
    }

    public static List<(JsonElement value, string path)> FindProperties(
        JsonElement element,
        string propertyName,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(JsonElement, string)>();

        Walk(element, "$", (current, path) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (current.TryGetProperty(propertyName, out var propValue))
            {
                results.Add((propValue, $"{path}.{propertyName}"));
            }
            return true;
        }, cancellationToken);

        return results;
    }

    public static List<(JsonElement value, string path)> FindByPathPattern(
        JsonElement element,
        string pathPattern,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(JsonElement, string)>();
        var parts = pathPattern.Split('.');

        Walk(element, "$", (current, path) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (MatchesPathPattern(path, parts))
            {
                JsonElement? target = TryResolvePath(current, parts);
                if (target.HasValue)
                {
                    results.Add((target.Value, $"{path}.{string.Join(".", parts)}"));
                }
            }
            return true;
        }, cancellationToken);

        return results;
    }

    public static List<(JsonElement value, string path)> FindEventsObjects(
        JsonElement element,
        CancellationToken cancellationToken = default)
        => FindProperties(element, "events", cancellationToken);

    public static List<(JsonElement value, string path)> FindLayoutObjects(
        JsonElement element,
        CancellationToken cancellationToken = default)
        => FindProperties(element, "layout", cancellationToken);

    public static List<(string propertyName, JsonElement value, string path)> FindEventFunctionArrays(
        JsonElement element,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string, JsonElement, string)>();

        var eventsList = FindEventsObjects(element, cancellationToken);
        foreach (var (eventsElement, eventsPath) in eventsList)
        {
            foreach (var property in eventsElement.EnumerateObject())
            {
                if (property.Name == "bindings")
                    continue;

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    results.Add((property.Name, property.Value, $"{eventsPath}.{property.Name}"));
                }
            }
        }

        return results;
    }

    public static List<(string value, string path)> FindStringFieldValues(
        JsonElement element,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string, string)>();

        Walk(element, "$", (current, path) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (current.TryGetProperty(fieldName, out var fieldValue) &&
                fieldValue.ValueKind == JsonValueKind.String)
            {
                var strValue = fieldValue.GetString();
                if (strValue != null)
                {
                    results.Add((strValue, $"{path}.{fieldName}"));
                }
            }
            return true;
        }, cancellationToken);

        return results;
    }

    public static List<(JsonElement array, string path)> FindBindingsParameters(
        JsonElement element,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(JsonElement, string)>();

        Walk(element, "$", (current, path) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (current.TryGetProperty("bindings", out var bindings) &&
                bindings.ValueKind == JsonValueKind.Object &&
                bindings.TryGetProperty("parameters", out var parameters) &&
                parameters.ValueKind == JsonValueKind.Array)
            {
                results.Add((parameters, $"{path}.bindings.parameters"));
            }
            return true;
        }, cancellationToken);

        return results;
    }

    public static List<(string value, string path)> FindBindingsOutput(
        JsonElement element,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string, string)>();

        Walk(element, "$", (current, path) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (current.TryGetProperty("bindings", out var bindings) &&
                bindings.ValueKind == JsonValueKind.Object &&
                bindings.TryGetProperty("output", out var output) &&
                output.ValueKind == JsonValueKind.String)
            {
                var outputValue = output.GetString();
                if (!string.IsNullOrEmpty(outputValue))
                {
                    results.Add((outputValue, $"{path}.bindings.output"));
                }
            }
            return true;
        }, cancellationToken);

        return results;
    }

    private static bool MatchesPathPattern(string currentPath, string[] patternParts)
    {
        var pathParts = currentPath.TrimStart('$').TrimStart('.').Split('.');
        if (pathParts.Length < patternParts.Length)
            return false;

        for (int i = 0; i < patternParts.Length; i++)
        {
            if (pathParts[pathParts.Length - 1 - i] != patternParts[patternParts.Length - 1 - i])
                return false;
        }
        return true;
    }

    private static JsonElement? TryResolvePath(JsonElement element, string[] parts)
    {
        JsonElement current = element;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!current.TryGetProperty(part, out current))
                return null;
        }
        return current;
    }
}
