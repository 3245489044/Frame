#nullable enable

namespace FrameworkAbstractions.Patterns;

public class TypedAdapterRegistry<TAdapter>
{
    protected readonly Dictionary<Type, TAdapter> _adapters = new();

    public void Register(Type type, TAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(type);
        _adapters[type] = adapter;
    }

    public TAdapter? Find(Type controlType)
    {
        if (_adapters.TryGetValue(controlType, out var entry))
            return entry;

        foreach (var kvp in _adapters)
        {
            if (kvp.Key.IsAssignableFrom(controlType))
                return kvp.Value;
        }

        return default;
    }

    public void Clear() => _adapters.Clear();

    public int Count => _adapters.Count;
}