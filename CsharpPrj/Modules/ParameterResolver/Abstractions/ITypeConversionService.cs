
using TConfig;

namespace ParameterResolver.Abstractions;

public interface ITypeConversionService
{
    ConversionResult Convert(object? value, Type targetType, ConversionOptions? options = null);

    ConversionResult ConvertBindings(
        IReadOnlyList<ParameterBinding> bindings,
        Func<string, object?> valueProvider,
        ConversionOptions? options = null);
}



