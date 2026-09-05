using System.Diagnostics;
using System.Reflection;
using ParameterResolver.Converter;
using TConfig;

namespace ParameterResolver.Parameters;

public static class ParameterHelper
{
    private static readonly Lazy<TypeConversionService> _fallbackConversionService = new(() =>
    {
        var registry = new ConverterRegistry();
        registry.RegisterProvider(new BuiltInConverterProvider());
        return new TypeConversionService(registry);
    });

    public static object GetParamDefault(ParameterInfo param) =>
        param.HasDefaultValue ? param.DefaultValue! : GetTypeDefault(param.ParameterType);

    public static object GetParamDefault(IParamConfig param) =>
        param.DefaultValue ?? GetTypeDefault(param.Type);

    public static object GetTypeDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null!;

    public static object ConvertParameter(ParameterInfo param, object? rawValue, ITypeConverter? typeConverter = null)
        => ConvertParameterCore(rawValue, param.ParameterType, () => GetParamDefault(param), typeConverter);

    public static object ConvertParameter(IParamConfig param, object? rawValue, ITypeConverter? typeConverter = null)
        => ConvertParameterCore(rawValue, param.Type, () => GetParamDefault(param), typeConverter);

    private static object ConvertParameterCore(object? rawValue, Type targetType, Func<object> getDefault, ITypeConverter? typeConverter)
    {
        if (rawValue == null) return getDefault();
        if (targetType == rawValue.GetType()) return rawValue;

        if (typeConverter != null)
        {
            var (converted, error) = typeConverter.Convert(rawValue, targetType);
            if (error == null && converted != null)
                return converted;

            Debug.WriteLine($"[ParameterHelper] ITypeConverter failed for '{rawValue.GetType().Name}'→'{targetType.Name}': {error}");
        }

        var fallbackResult = _fallbackConversionService.Value.Convert(rawValue, targetType);
        if (fallbackResult.IsSuccess)
            return fallbackResult.Value!;

        Debug.WriteLine($"[ParameterHelper] TypeConversionService also failed for '{rawValue.GetType().Name}'→'{targetType.Name}': {fallbackResult.Error}");

        return getDefault();
    }
}
