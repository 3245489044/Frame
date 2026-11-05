
using System.Text.Json.Nodes;

namespace Modules.Parsing.Parsing
{
    public static class PlaceholderResolver
    {
        public static void ReplacePlaceholders(JsonNode node, string paramName, string value)
        {
            var placeholder = "${" + paramName + "}";

            switch (node)
            {
                case JsonObject obj:
                    foreach (var kv in obj.ToList())
                    {
                        if (kv.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var strValue))
                        {
                            if (strValue.Contains(placeholder))
                            {
                                obj[kv.Key] = JsonValue.Create(strValue.Replace(placeholder, value));
                            }
                        }
                        else if (kv.Value is JsonObject || kv.Value is JsonArray)
                        {
                            if (kv.Value != null)
                            {
                                ReplacePlaceholders(kv.Value, paramName, value);
                            }
                        }
                    }
                    break;

                case JsonArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i] is JsonValue arrVal && arrVal.TryGetValue<string>(out var arrStr))
                        {
                            if (arrStr.Contains(placeholder))
                            {
                                arr[i] = JsonValue.Create(arrStr.Replace(placeholder, value));
                            }
                        }
                        else if (arr[i] is JsonObject || arr[i] is JsonArray)
                        {
                            if (arr[i] != null)
                            {
                                ReplacePlaceholders(arr[i], paramName, value);
                            }
                        }
                    }
                    break;
            }
        }

        public static string ReplaceInString(string input, string paramName, string value)
        {
            var placeholder = "${" + paramName + "}";
            return input.Contains(placeholder)
                ? input.Replace(placeholder, value)
                : input;
        }

        public static string ReplaceAllInString(string input, Dictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(input) || parameters == null || parameters.Count == 0)
                return input;

            foreach (var kvp in parameters)
            {
                var placeholder = "${" + kvp.Key + "}";
                if (input.Contains(placeholder))
                {
                    input = input.Replace(placeholder, kvp.Value);
                }
            }
            return input;
        }
    }
}
