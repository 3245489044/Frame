
namespace ValidationFramework.Providers;

public interface IObjectRegistry
{
    string Name { get; }

    bool HasItem(string name);

    IReadOnlyCollection<string> GetItems();

    string? GetItemType(string name);
}

public interface IContractSignatureProvider
{
    IReadOnlyList<string>? GetParameterTypes(string functionName);

    bool HasFunction(string functionName);

    IReadOnlyCollection<string> GetFunctions();
}

public class CompositeValidationProvider : IObjectRegistry, IContractSignatureProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _functions;
    private readonly IReadOnlyCollection<string> _objects;

    public string Name { get; }

    public CompositeValidationProvider(
        string name,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? functions = null,
        IEnumerable<string>? objects = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _functions = functions ?? new Dictionary<string, IReadOnlyList<string>>();
        _objects = objects?.ToList().AsReadOnly()
            ?? new List<string>().AsReadOnly();
    }

    public bool HasItem(string name)
        => _functions.ContainsKey(name) || _objects.Contains(name);

    public IReadOnlyCollection<string> GetItems()
        => _functions.Keys.Concat(_objects).Distinct().ToList().AsReadOnly();

    public string? GetItemType(string name)
        => _functions.TryGetValue(name, out var types)
            ? $"function[{types.Count}]"
            : (_objects.Contains(name) ? "object" : null);

    public IReadOnlyList<string>? GetParameterTypes(string functionName)
        => _functions.TryGetValue(functionName, out var types) ? types : null;

    public bool HasFunction(string functionName)
        => _functions.ContainsKey(functionName);

    public IReadOnlyCollection<string> GetFunctions()
        => _functions.Keys.ToList().AsReadOnly();
}