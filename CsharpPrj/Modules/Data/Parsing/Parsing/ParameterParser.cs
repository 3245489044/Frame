using System.Globalization;

namespace Modules.Parsing.Parsing;

public static class ParameterParser
{
    private static readonly HashSet<string> TrueValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "yes", "1", "on", "enabled"
    };

    private static readonly HashSet<string> FalseValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "false", "no", "0", "off", "disabled"
    };

    public static object ParseValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return null!;

        if (TrueValues.Contains(value))
            return true;

        if (FalseValues.Contains(value))
            return false;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return intValue;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return longValue;

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var doubleValue))
            return doubleValue;

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
            return decimalValue;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue))
            return dateValue;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOffsetValue))
            return dateOffsetValue;

        if (Guid.TryParse(value, out var guidValue))
            return guidValue;

        return value;
    }

    public static Dictionary<string, object> ParseParameters(List<string> tokens, int startIndex, bool useStringTrue = false)
    {
        var parameters = new Dictionary<string, object>();

        for (int i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var equalsIndex = token.IndexOf('=');

            if (equalsIndex > 0)
            {
                var key = token.Substring(0, equalsIndex).Trim();
                var value = token.Substring(equalsIndex + 1).Trim();
                parameters[key] = ParseValue(value);
            }
            else
            {
                parameters[token] = useStringTrue ? "true" : true;
            }
        }

        return parameters;
    }
}
