
using System.Text.Json;

namespace ParameterResolver.Formatting;

public static class ValueFormatter
{
    public static string Format(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            bool b => b.ToString(),
            _ => JsonSerializer.Serialize(value)
        };
    }

    public static string Format(object? value, string? format)
    {
        if (string.IsNullOrEmpty(format))
            return Format(value);

        return value switch
        {
            null => string.Empty,
            string s => s,
            DateTime dt => dt.ToString(format),
            IFormattable f => f.ToString(format, null),
            _ => Format(value)
        };
    }
}
