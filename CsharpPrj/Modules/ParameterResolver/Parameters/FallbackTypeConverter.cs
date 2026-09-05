using TConfig;

namespace ParameterResolver.Parameters;

public class FallbackTypeConverter : ITypeConverter
{
    public (object? converted, string? error) Convert(object? value, Type targetType)
    {
        if (value == null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                return (null, $"Cannot convert null to non-nullable type '{targetType.Name}'");
            return (null, null);
        }

        if (targetType.IsInstanceOfType(value))
            return (value, null);

        try
        {
            var result = System.Convert.ChangeType(value, targetType);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, $"Convert.ChangeType failed: {ex.Message}");
        }
    }
}
