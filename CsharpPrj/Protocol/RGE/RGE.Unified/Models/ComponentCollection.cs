namespace Rge.Unified.Models;

public static class ComponentKeys
{
    public const string FuncRef = "funcRef";

    public const string StepType = "stepType";

    public const string DefaultStepType = "Function";

    public const string OutputKey = "outputKey";

    public const string Policy = "policy";

    public const string Metadata = "metadata";
}

public sealed class ResiliencePolicy
{
    public int RetryCount { get; init; }

    public int RetryDelayMs { get; init; } = 1000;

    public int TimeoutMs { get; init; }
}

public sealed class ComponentCollection
{
    private readonly Dictionary<string, object?> _components = new(StringComparer.OrdinalIgnoreCase);

    public object? Get(string key) => _components.TryGetValue(key, out var v) ? v : null;

    public T? Get<T>(string key) => Get(key) is T t ? t : default;

    public ComponentCollection Set(string key, object? value)
    {
        _components[key] = value;
        return this;
    }

    public bool Contains(string key) => _components.ContainsKey(key);

    public bool Remove(string key) => _components.Remove(key);

    public IEnumerable<string> Keys => _components.Keys;

    public int Count => _components.Count;
}