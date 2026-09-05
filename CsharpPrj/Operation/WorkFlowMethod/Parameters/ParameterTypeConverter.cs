
using ParameterBinding = TConfig.ParameterBinding;
using WorkFlowMethod.Parameters.Abstractions;
using ITypeConversionService = ParameterResolver.Abstractions.ITypeConversionService;
using ConversionOptions = ParameterResolver.Abstractions.ConversionOptions;

namespace WorkFlowMethod.Parameters;

public class ParameterTypeConverter : IParameterTypeConverter
{
    private readonly ITypeConversionService? _typeConversionService;

    public static readonly ParameterTypeConverter Default = new();

    public ParameterTypeConverter(ITypeConversionService? typeConversionService = null)
    {
        _typeConversionService = typeConversionService;
    }

    public object Convert(ParameterBinding binding, object? resolvedValue)
    {
        if (binding == null || resolvedValue == null)
            return resolvedValue!;

        var typeName = binding.Type;
        if (string.IsNullOrEmpty(typeName))
            return resolvedValue;

        return ConvertTo(resolvedValue, typeName);
    }

    public object ConvertTo(object value, string targetTypeName)
    {
        if (value == null || string.IsNullOrEmpty(targetTypeName))
            return value!;

        var targetType = ParameterResolver.Converter.TypeResolver.Resolve(targetTypeName);

        if (_typeConversionService != null && targetType != null)
        {
            var result = _typeConversionService.Convert(value, targetType);
            if (result.IsSuccess && result.Value != null)
                return result.Value;

            WorkFlowMethodLog.Log("ParameterTypeConverter_Convert_Failed",
                new { value = value.ToString(), targetType = targetTypeName, error = result.Error });
            return value!;
        }

        try
        {
            return System.Convert.ChangeType(value, targetType ?? typeof(string));
        }
        catch (Exception ex)
        {
            WorkFlowMethodLog.Log("ParameterTypeConverter_ChangeType_Failed",
                new { value = value.ToString(), targetType = targetTypeName, error = ex.Message });
            return value;
        }
    }

    public string GetFallbackValue(ParameterBinding binding, object? resolvedValue)
    {
        return resolvedValue?.ToString() ?? binding?.Source ?? string.Empty;
    }
}
