
using System.Text.Json;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Converter;

public class StringToListConverter : IValueConverter
{
    public Type TargetType => typeof(List<string>);

    public object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context)
    {
        if (!inputs.TryGetValue("value", out var rawValue) || rawValue == null)
            return new List<string>();

        return Parse(rawValue);
    }

    public static List<string> Parse(object value)
    {
        var result = new List<string>();
        var valueStr = value.ToString() ?? string.Empty;

        if (value.GetType().Name == "ListData")
        {
            var itemsProp = value.GetType().GetProperty("Items");
            if (itemsProp?.GetValue(value) is string[] items)
            {
                result.AddRange(items.Where(s => !string.IsNullOrEmpty(s)));
                return result;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(valueStr);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        result.Add(element.GetString() ?? string.Empty);
                    }
                    else if (element.ValueKind == JsonValueKind.Object)
                    {
                        if (element.TryGetProperty("Text", out var textProp) &&
                            textProp.ValueKind == JsonValueKind.String)
                        {
                            result.Add(textProp.GetString() ?? string.Empty);
                        }
                        else
                        {
                            result.Add(element.ToString());
                        }
                    }
                    else
                    {
                        result.Add(element.ToString());
                    }
                }
                return result;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in prop.Value.EnumerateArray())
                        {
                            if (element.ValueKind == JsonValueKind.String)
                                result.Add(element.GetString() ?? string.Empty);
                            else
                                result.Add(element.ToString());
                        }
                        if (result.Count > 0)
                            return result;
                    }
                }
            }
        }
        catch
        {
        }

        if (result.Count == 0 && !string.IsNullOrEmpty(valueStr))
        {
            if (valueStr.Contains('\n'))
            {
                result.AddRange(valueStr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim('\r', ' ', '\t'))
                    .Where(s => !string.IsNullOrEmpty(s)));
            }
            else if (valueStr.Contains(','))
            {
                result.AddRange(valueStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s)));
            }
            else
            {
                result.Add(valueStr);
            }
        }

        return result;
    }
}
