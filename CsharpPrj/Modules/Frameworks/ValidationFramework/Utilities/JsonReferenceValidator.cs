
using System.Text.Json;
using ValidationFramework.Abstractions;

namespace ValidationFramework.Utilities;

public static class JsonReferenceValidator
{
    public static IReadOnlyList<(string Name, string Path)> FindStringPropertyReferences(
        JsonElement root,
        string propertyName,
        int? maxDepth = null,
        string rootPath = "$")
    {
        var results = new List<(string, string)>();
        foreach (var (_, value, path) in JsonTreeWalker.FindProperties(root, propertyName, maxDepth, rootPath))
        {
            AddStringResult(results, value, path);
        }
        return results;
    }

    public static IReadOnlyList<(string Name, string Path)> FindStringReferencesInContainerArrays(
        JsonElement root,
        string containerPropertyName)
    {
        var results = new List<(string, string)>();
        foreach (var (_, containerValue, containerPath) in JsonTreeWalker.FindProperties(root, containerPropertyName))
        {
            if (containerValue.ValueKind != JsonValueKind.Object) continue;

            foreach (var prop in containerValue.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                var arrayPath = $"{containerPath}.{prop.Name}";
                var index = 0;
                foreach (var item in prop.Value.EnumerateArray())
                {
                    AddStringResult(results, item, $"{arrayPath}[{index}]");
                    index++;
                }
            }
        }
        return results;
    }

    public static List<(string Name, string Path)> CollectSourceReferences(
        JsonElement element,
        string path)
        => FindStringPropertyReferences(element, "source", rootPath: path).ToList();

    private static void AddStringResult(
        List<(string Name, string Path)> results,
        JsonElement value,
        string path)
    {
        var name = JsonTreeWalker.GetStringOrNull(value);
        if (string.IsNullOrWhiteSpace(name)) return;

        results.Add((name, path));
    }

    public static string? ExtractScopeFromPath(JsonElement config, string path)
    {
        foreach (var (_, value, scopePath) in JsonTreeWalker.FindProperties(config, "scope"))
        {
            if (value.ValueKind == JsonValueKind.String
                && IsPathSegmentPrefix(scopePath, path))
            {
                return value.GetString();
            }
        }
        return null;
    }

    public static bool IsPathSegmentPrefix(string prefix, string path)
    {
        if (string.Equals(prefix, "$", StringComparison.Ordinal)) return true;

        return path.Length > prefix.Length
            && path.StartsWith(prefix, StringComparison.Ordinal)
            && (path[prefix.Length] == '.' || path[prefix.Length] == '[');
    }

    public static void ValidateExistence(
        ICollection<ValidationDiagnostic> diagnostics,
        string ruleName,
        string filePath,
        IReadOnlyList<(string Name, string Path)> references,
        Func<string, bool> isRegistered,
        Func<IReadOnlyCollection<string>> getAvailableItems,
        string referenceKind,
        CancellationToken cancellationToken = default)
    {
        foreach (var reference in references)
        {
            ValidateExistence(
                diagnostics, ruleName, filePath, reference,
                isRegistered, getAvailableItems, referenceKind, cancellationToken);
        }
    }

    public static void ValidateExistence(
        ICollection<ValidationDiagnostic> diagnostics,
        string ruleName,
        string filePath,
        (string Name, string Path) reference,
        Func<string, bool> isRegistered,
        Func<IReadOnlyCollection<string>> getAvailableItems,
        string referenceKind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (name, path) = reference;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!isRegistered(name))
        {
            diagnostics.Add(
                ValidationDiagnostic.Error(
                    ruleName,
                    $"{referenceKind} '{name}' 未注册",
                    new SourceLocation(filePath, path),
                    $"可用{referenceKind}: {FormatAvailableItems(getAvailableItems())}"));
        }
    }

    public static string FormatAvailableItems(IEnumerable<string> items, int maxItems = 10)
        => string.Join(", ", items.Take(maxItems));
}