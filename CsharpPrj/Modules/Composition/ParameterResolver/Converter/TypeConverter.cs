
using ParameterResolver.Abstractions;

namespace ParameterResolver.Converter;

public static class TypeConverter
{
    private static readonly Lazy<TypeConversionService> _service = new(() =>
    {
        var registry = new ConverterRegistry();
        registry.RegisterProvider(new BuiltInConverterProvider());
        return new TypeConversionService(registry);
    });

    private static TypeConversionService Service => _service.Value;

    public static (object? converted, string? error) Convert(object? value, Type targetType)
    {
        var result = Service.Convert(value, targetType);
        return (result.Value, result.IsSuccess ? null : result.Error);
    }

    public static object?[] ConvertArguments(object?[] arguments, Type[] targetTypes)
    {
        if (arguments == null || targetTypes == null)
            throw new ArgumentNullException(arguments == null ? nameof(arguments) : nameof(targetTypes));

        if (arguments.Length != targetTypes.Length)
            throw new ArgumentException(
                $"Argument count mismatch: {arguments.Length} arguments vs {targetTypes.Length} target types");

        var result = new object?[arguments.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            var (converted, error) = Convert(arguments[i], targetTypes[i]);
            if (error != null)
                throw new InvalidOperationException(
                    $"Argument[{i}] conversion failed: {error}");
            result[i] = converted;
        }
        return result;
    }

    public static void RegisterParser<T>(Func<string?, (bool, object?)> parser)
    {
        Service.RegisterParser<T>(parser);
    }

    public static bool CanConvert(Type sourceType, Type targetType)
    {
        object? testValue = GetTestValue(sourceType);
        var result = Service.Convert(testValue, targetType);
        return result.IsSuccess;
    }

    private static object? GetTestValue(Type type)
    {
        if (type == typeof(string))
            return "0";
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return null;
    }
}
