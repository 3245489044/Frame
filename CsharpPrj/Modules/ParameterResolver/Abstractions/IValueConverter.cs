
namespace ParameterResolver.Abstractions;

public interface IValueConverter
{
    Type TargetType { get; }

    object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context);
}

public class ConversionContext
{
    public required Type TargetType { get; init; }

    public ConversionOptions? Options { get; init; }

    public ITypeConversionService? ConversionService { get; init; }

    public IServiceProvider? Services { get; init; }
}
