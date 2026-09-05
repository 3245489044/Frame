using System.Reflection;
using TConfig;

namespace ExecutionFramework.Engine;

public static class ParameterHelper
{
    public static object GetParamDefault(ParameterInfo param) =>
        param.HasDefaultValue ? param.DefaultValue! : GetTypeDefault(param.ParameterType);

    public static object GetParamDefault(IParamConfig param) =>
        param.DefaultValue ?? GetTypeDefault(param.Type);

    public static object GetTypeDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null!;

    public static object ConvertParameter(ParameterInfo param, object? rawValue, ITypeConverter? typeConverter = null)
    {
        if (rawValue == null) return GetParamDefault(param);
        return ConvertCore(param.ParameterType, rawValue, typeConverter);
    }

    public static object ConvertParameter(IParamConfig param, object? rawValue, ITypeConverter? typeConverter = null)
    {
        if (rawValue == null) return GetParamDefault(param);
        return ConvertCore(param.Type, rawValue, typeConverter);
    }

    private static object ConvertCore(Type targetType, object rawValue, ITypeConverter? typeConverter)
    {
        if (targetType == rawValue.GetType()) return rawValue;

        if (typeConverter != null)
        {
            var (converted, error) = typeConverter.Convert(rawValue, targetType);
            if (error == null && converted != null)
                return converted;
        }

        try { return Convert.ChangeType(rawValue, targetType); }
        catch { return rawValue; }
    }
}
