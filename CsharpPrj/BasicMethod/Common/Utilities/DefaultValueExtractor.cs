
using System.Reflection;

namespace Modules.Common.Utilities
{
    public class DefaultValueExtractor : IValueExtractor
    {
        public string? ExtractValue(object? parameters, string key)
        {
            if (parameters == null || string.IsNullOrEmpty(key)) return null;

            if (parameters is Dictionary<string, object?> dict)
                return dict.TryGetValue(key, out var val) ? val?.ToString() : null;

            if (parameters is Dictionary<string, string> strDict)
                return strDict.TryGetValue(key, out var val) ? val : null;

            if (parameters is IReadOnlyDictionary<string, object?> readOnlyDict)
                return readOnlyDict.TryGetValue(key, out var val) ? val?.ToString() : null;

            var type = parameters.GetType();
            var prop = type.GetProperty(key);
            if (prop != null && prop.CanRead)
            {
                var value = prop.GetValue(parameters);
                return value?.ToString();
            }

            var field = type.GetField(key);
            if (field != null)
            {
                var value = field.GetValue(parameters);
                return value?.ToString();
            }

            return null;
        }

        public IReadOnlyDictionary<string, string?> ExtractAllValues(object? parameters)
        {
            var result = new Dictionary<string, string?>();

            if (parameters == null) return result;

            if (parameters is Dictionary<string, object?> dict)
            {
                foreach (var kvp in dict) result[kvp.Key] = kvp.Value?.ToString();
                return result;
            }

            if (parameters is Dictionary<string, string> strDict)
            {
                foreach (var kvp in strDict) result[kvp.Key] = kvp.Value;
                return result;
            }

            if (parameters is IReadOnlyDictionary<string, object?> readOnlyDict)
            {
                foreach (var kvp in readOnlyDict) result[kvp.Key] = kvp.Value?.ToString();
                return result;
            }

            var type = parameters.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (prop.CanRead)
                {
                    var value = prop.GetValue(parameters);
                    result[prop.Name] = value?.ToString();
                }
            }

            foreach (var field in type.GetFields())
            {
                if (!result.ContainsKey(field.Name))
                {
                    var value = field.GetValue(parameters);
                    result[field.Name] = value?.ToString();
                }
            }

            return result;
        }
    }
}
