
#nullable enable
using System.Text.Json;

namespace UniversalMiddleware.Extensions;

public static class JsonElementExtensions
{
    public static Dictionary<string, object> ToDictionary(this JsonElement element)
    {
        var dict = new Dictionary<string, object>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = ConvertToObject(property.Value);
            }
        }

        return dict;
    }

    public static object ConvertToObject(this JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.ToDictionary(),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertToObject).ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => TryGetNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetString()!
        };
    }

    public static object TryGetNumber(this JsonElement element)
    {
        if (element.TryGetInt64(out long longValue)) return longValue;
        if (element.TryGetDouble(out double doubleValue)) return doubleValue;
        return element.GetString()!;
    }

    public static object[] TryParseJsonArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<object>();
        try
        {
            var parsed = JsonSerializer.Deserialize<object[]>(value);
            return parsed ?? Array.Empty<object>();
        }
        catch
        {
            return new object[] { value.Trim().Trim('"') };
        }
    }

    public static object TryParseJsonToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            return JsonSerializer.Deserialize<object>(value) ?? value;
        }
        catch
        {
            return value;
        }
    }

    public static List<string> ParseArrayContent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(value);
            if (parsed != null) return parsed;
        }
        catch
        {
        }
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim().Trim('"'))
                   .Where(s => !string.IsNullOrEmpty(s))
                   .ToList();
    }

    public class ValidationResult
    {
        private readonly List<string> _errors = new List<string>();

        public bool IsValid => _errors.Count == 0;
        public IReadOnlyList<string> Errors => _errors;

        public void AddError(string error) => _errors.Add(error);

        public string GetErrorString() => string.Join(Environment.NewLine, _errors);
    }
}
