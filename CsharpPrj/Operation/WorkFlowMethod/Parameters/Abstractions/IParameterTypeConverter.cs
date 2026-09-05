
using ParameterBinding = TConfig.ParameterBinding;

namespace WorkFlowMethod.Parameters.Abstractions;

public interface IParameterTypeConverter
{
    object Convert(ParameterBinding binding, object? resolvedValue);

    object ConvertTo(object value, string targetTypeName);

    string GetFallbackValue(ParameterBinding binding, object? resolvedValue);
}
