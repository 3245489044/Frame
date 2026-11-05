
using ParameterResolver.Abstractions;

namespace ParameterResolver.Binding;

public interface IBindingResolver<TBinding>
{
    string Name { get; }

    IReadOnlyList<ParameterMapping> ResolveParameters(TBinding binding, string functionName);

    IReadOnlyDictionary<string, IReadOnlyList<ParameterMapping>> ResolveAllParameters(TBinding binding);
}
