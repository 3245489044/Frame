
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ParameterResolver.Abstractions;
using TConfig;

namespace ParameterResolver.Converter;

public class TypeConversionService : ITypeConversionService
{
    private readonly IConverterRegistry _registry;

    private static readonly Dictionary<Type, Func<string?, (bool, object?)>> BuiltInParsers = new()
    {
        [typeof(string)]   = (s) => (true, s),
        [typeof(int)]      = (s) => (int.TryParse(s, out var v), v),
        [typeof(long)]     = (s) => (long.TryParse(s, out var v), v),
        [typeof(short)]    = (s) => (short.TryParse(s, out var v), v),
        [typeof(byte)]     = (s) => (byte.TryParse(s, out var v), v),
        [typeof(float)]    = (s) => (float.TryParse(s, out var v), v),
        [typeof(double)]   = (s) => (double.TryParse(s, out var v), v),
        [typeof(decimal)]  = (s) => (decimal.TryParse(s, out var v), v),
        [typeof(bool)]     = (s) => (bool.TryParse(s, out var v), v),
        [typeof(char)]     = (s) => (char.TryParse(s, out var v), v),
        [typeof(DateTime)] = (s) => (DateTime.TryParse(s, out var v), v),
        [typeof(DateTimeOffset)] = (s) => (DateTimeOffset.TryParse(s, out var v), v),
        [typeof(TimeSpan)] = (s) => (TimeSpan.TryParse(s, out var v), v),
        [typeof(Guid)]     = (s) => (Guid.TryParse(s, out var v), v),
        [typeof(Version)]  = (s) => (Version.TryParse(s, out var v), v),
        [typeof(Uri)]      = (s) => (Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var v), v),
    };

    public TypeConversionService(IConverterRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ConversionResult Convert(object? value, Type targetType, ParameterResolver.Abstractions.ConversionOptions? options = null)
    {
        if (targetType == null)
            return ConversionResult.Failure("Target type is null");

        options ??= ParameterResolver.Abstractions.ConversionOptions.Default;

        try
        {
            if (value == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return ConversionResult.Failure(
                        $"Cannot convert null to non-nullable type '{targetType.Name}'");
                return ConversionResult.Success(null);
            }

            if (targetType.IsInstanceOfType(value))
                return ConversionResult.Success(value);

            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
                return Convert(value, underlyingType, options);

            if (targetType.IsEnum)
            {
                var result = ConvertToEnum(value, targetType);
                if (result.IsSuccess)
                    return result;
            }

            var stringValue = value.ToString();
            if (stringValue == null)
                return ConversionResult.Failure($"Value '{value}' ToString() returned null");

            var customConverter = _registry.GetConverter(targetType);
            if (customConverter != null)
            {
                var context = new ConversionContext
                {
                    TargetType = targetType,
                    Options = options,
                    ConversionService = this
                };
                var convertedValue = customConverter.Convert(
                    new Dictionary<string, object?> { ["value"] = value }, context);
                return ConversionResult.Success(convertedValue);
            }

            if (BuiltInParsers.TryGetValue(targetType, out var parser))
            {
                var (success, parsedValue) = parser(stringValue);
                if (success)
                    return ConversionResult.Success(parsedValue);
                return ConversionResult.Failure(
                    $"Cannot convert '{stringValue}' to {targetType.Name}");
            }

            try
            {
                if (value is IConvertible)
                {
                    var culture = options.GetEffectiveCulture();
                    var result = System.Convert.ChangeType(value, targetType, culture);
                    return ConversionResult.Success(result);
                }
            }
            catch { }

            try
            {
                var converter = TypeDescriptor.GetConverter(targetType);
                if (converter.CanConvertFrom(value.GetType()))
                {
                    var culture = options.GetEffectiveCulture();
                    var result = converter.ConvertFrom(null, culture, value);
                    return ConversionResult.Success(result);
                }
            }
            catch { }

            try
            {
                if (value is string json)
                {
                    var result = JsonSerializer.Deserialize(json, targetType);
                    if (result != null)
                        return ConversionResult.Success(result);
                }
            }
            catch { }

            return ConversionResult.Failure(
                $"Cannot convert value '{value}' (type: {value.GetType().Name}) to {targetType.Name}");
        }
        catch (Exception ex) when (!options.ThrowOnError)
        {
            return ConversionResult.Failure(
                $"Conversion failed: {ex.Message}");
        }
    }

    public ConversionResult ConvertBindings(
        IReadOnlyList<ParameterBinding> bindings,
        Func<string, object?> valueProvider,
        ParameterResolver.Abstractions.ConversionOptions? options = null)
    {
        if (bindings == null)
            return ConversionResult.Failure("Bindings is null");

        if (valueProvider == null)
            return ConversionResult.Failure("ValueProvider is null");

        options ??= ParameterResolver.Abstractions.ConversionOptions.Default;
        var result = new Dictionary<string, object?>();

        foreach (var binding in bindings)
        {
            try
            {
                object? rawValue = null;

                if (!string.IsNullOrEmpty(binding.Source))
                {
                    rawValue = valueProvider(binding.Source);
                }

                if (rawValue == null && !string.IsNullOrEmpty(binding.DefaultValue))
                {
                    rawValue = binding.DefaultValue;
                }

                if (binding.Required && rawValue == null)
                {
                    return ConversionResult.Failure(
                        $"Required binding '{binding.Name}' has no value (source: '{binding.Source}')");
                }

                var key = !string.IsNullOrEmpty(binding.Name) ? binding.Name : binding.Source ?? "unknown";
                result[key] = rawValue?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (!options.ThrowOnError)
            {
                return ConversionResult.Failure(
                    $"Failed to convert binding '{binding.Name ?? binding.Source}': {ex.Message}");
            }
        }

        return ConversionResult.Success(result);
    }

    public void RegisterParser<T>(Func<string?, (bool, object?)> parser)
    {
        BuiltInParsers[typeof(T)] = parser;
    }

    private static ConversionResult ConvertToEnum(object value, Type enumType)
    {
        var stringValue = value.ToString();

        if (Enum.TryParse(enumType, stringValue, ignoreCase: true, out var enumResult))
            return ConversionResult.Success(enumResult);

        if (int.TryParse(stringValue, out var intValue))
        {
            var hasFlags = enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;
            if (hasFlags)
                return ConversionResult.Success(Enum.ToObject(enumType, intValue));
            if (Enum.IsDefined(enumType, intValue))
                return ConversionResult.Success(Enum.ToObject(enumType, intValue));
        }

        return ConversionResult.Failure($"Cannot convert '{stringValue}' to enum {enumType.Name}");
    }
}
