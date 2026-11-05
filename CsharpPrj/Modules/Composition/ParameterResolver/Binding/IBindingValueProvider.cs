
using ParameterResolver.Abstractions;

namespace ParameterResolver.Binding;

public interface IBindingValueProvider<TBinding>
{
    string Name { get; }

    object? GetValue(TBinding binding, ParameterMapping mapping, object? context);

    IReadOnlyDictionary<string, object?> GetValues(TBinding binding, IReadOnlyList<ParameterMapping> mappings, object? context);
}
