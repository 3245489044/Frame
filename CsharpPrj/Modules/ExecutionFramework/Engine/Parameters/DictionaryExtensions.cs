namespace ExecutionFramework.Engine;

internal static class DictionaryExtensions
{
    public static Dictionary<string, object> ToNonNullDictionary(
        this IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach (var kvp in source)
        {
            if (kvp.Value != null)
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    public static Dictionary<string, object> ToObjectDictionary(
        this IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach (var kvp in source)
            result[kvp.Key] = kvp.Value!;
        return result;
    }
}
