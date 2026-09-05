
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Converter;

public class BuiltInConverterProvider : IConverterProvider
{
    public string Name => "BuiltIn";

    public IEnumerable<KeyValuePair<Type, IValueConverter>> GetConverters()
    {
        yield break;
    }
}

public class AssemblyScannerProvider : IConverterProvider
{
    private readonly bool _scanAllAssemblies;

    public string Name => "AssemblyScanner";

    public AssemblyScannerProvider(bool scanAllAssemblies = false)
    {
        _scanAllAssemblies = scanAllAssemblies;
    }

    public IEnumerable<KeyValuePair<Type, IValueConverter>> GetConverters()
    {
        if (!_scanAllAssemblies)
            yield break;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttributes(typeof(TypeConverterAttribute), false)
                    .Cast<TypeConverterAttribute>()
                    .FirstOrDefault();

                if (attr != null && !string.IsNullOrEmpty(attr.ConverterTypeName))
                {
                    var converterType = TypeResolver.Resolve(attr.ConverterTypeName);
                    if (converterType != null && typeof(IValueConverter).IsAssignableFrom(converterType))
                    {
                        if (Activator.CreateInstance(converterType) is IValueConverter converter)
                        {
                            yield return new KeyValuePair<Type, IValueConverter>(type, converter);
                        }
                    }
                }
            }
        }
    }
}

public class TryParseConverter : IValueConverter
{
    private readonly Type _targetType;
    private readonly Func<string?, (bool, object?)> _parser;

    public Type TargetType => _targetType;

    public TryParseConverter(Type targetType, Func<string?, (bool, object?)> parser)
    {
        _targetType = targetType;
        _parser = parser;
    }

    public object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context)
    {
        if (!inputs.TryGetValue("value", out var rawValue) || rawValue == null)
            return null;

        var stringValue = rawValue.ToString();
        if (stringValue == null)
            return null;

        var (success, result) = _parser(stringValue);
        return success ? result : throw new InvalidOperationException(
            $"Cannot convert '{stringValue}' to {_targetType.Name}");
    }
}

public class TypeDescriptorConverter : IValueConverter
{
    private readonly Type _targetType;

    public Type TargetType => _targetType;

    public TypeDescriptorConverter(Type targetType)
    {
        _targetType = targetType;
    }

    public object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context)
    {
        if (!inputs.TryGetValue("value", out var rawValue) || rawValue == null)
            return null;

        var converter = TypeDescriptor.GetConverter(_targetType);
        if (converter.CanConvertFrom(rawValue.GetType()))
        {
            var culture = context.Options?.GetEffectiveCulture() ?? CultureInfo.InvariantCulture;
            return converter.ConvertFrom(null, culture, rawValue);
        }

        throw new InvalidOperationException(
            $"TypeDescriptor cannot convert '{rawValue}' to {_targetType.Name}");
    }
}

public class JsonConverter : IValueConverter
{
    private readonly Type _targetType;

    public Type TargetType => _targetType;

    public JsonConverter(Type targetType)
    {
        _targetType = targetType;
    }

    public object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context)
    {
        if (!inputs.TryGetValue("value", out var rawValue) || rawValue == null)
            return null;

        if (rawValue is string json)
        {
            var result = JsonSerializer.Deserialize(json, _targetType);
            return result ?? throw new InvalidOperationException(
                $"JsonSerializer returned null for '{json}' as {_targetType.Name}");
        }

        throw new InvalidOperationException(
            $"JsonConverter requires string input, got {rawValue.GetType().Name}");
    }
}
