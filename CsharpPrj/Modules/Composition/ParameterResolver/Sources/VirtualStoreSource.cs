
using ParameterResolver.Abstractions;

namespace ParameterResolver.Sources;

public class VirtualStoreSource : IParameterSource
{
    private readonly IReadOnlyDictionary<string, string?> _store;

    public VirtualStoreSource(IReadOnlyDictionary<string, string?> store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string? ReadValue(string controlName)
    {
        return _store.TryGetValue(controlName, out var value) ? value : null;
    }
}
