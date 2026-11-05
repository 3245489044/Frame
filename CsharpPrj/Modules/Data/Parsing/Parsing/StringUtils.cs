
using System.Text;
using System.Text.RegularExpressions;

namespace Modules.Parsing.Parsing
{
    public static class StringUtils
    {
        public static List<string> SplitEscaped(string input, char separator = ',')
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool escape = false;

            foreach (char c in input)
            {
                if (escape)
                {
                    current.Append(c);
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == separator)
                {
                    var item = current.ToString().Trim();
                    if (item.Length > 0)
                        result.Add(item);
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            var lastItem = current.ToString().Trim();
            if (lastItem.Length > 0)
                result.Add(lastItem);

            return result;
        }

        public static string Truncate(string str, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;
            return str[..maxLength] + suffix;
        }

        public static string FormatTemplate(string template, Dictionary<string, string>? parameters)
        {
            if (string.IsNullOrEmpty(template))
                return template ?? string.Empty;

            if (parameters == null || parameters.Count == 0)
                return template;

            return Regex.Replace(template, @"\{(\w+)\}", match =>
            {
                var key = match.Groups[1].Value;
                return parameters.TryGetValue(key, out var value) ? value : match.Value;
            });
        }

        public static Dictionary<string, string>? ConvertParameters(object? parameters)
        {
            if (parameters == null)
                return null;

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(parameters,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
                if (dict == null)
                    return null;

                return dict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? kvp.Value.GetString() ?? string.Empty
                        : kvp.Value.GetRawText()
                );
            }
            catch
            {
                return null;
            }
        }
    }
}


