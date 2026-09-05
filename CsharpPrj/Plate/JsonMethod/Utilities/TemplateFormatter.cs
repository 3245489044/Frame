
using System.Text.RegularExpressions;

namespace Plate.JsonMethod.Utilities
{
    public class TemplateFormatter
    {
        private readonly string _prefix;
        private readonly string _suffix;
        private readonly string _pattern;

        public TemplateFormatter(string prefix = "{", string suffix = "}")
        {
            _prefix = prefix;
            _suffix = suffix;
            _pattern = $"{Regex.Escape(prefix)}([^{{}}]+){Regex.Escape(suffix)}";
        }

        public string Format(string template, IDictionary<string, object?> parameters, string? missingPlaceholder = null)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            return Regex.Replace(template, _pattern, match =>
            {
                var key = match.Groups[1].Value;
                if (parameters.TryGetValue(key, out var value) && value != null)
                {
                    return value.ToString() ?? string.Empty;
                }
                return missingPlaceholder ?? match.Value;
            });
        }

        public string FormatFromObject(string template, object parameters, string? missingPlaceholder = null)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            var dict = new Dictionary<string, object?>();
            foreach (var prop in parameters.GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                dict[prop.Name] = prop.GetValue(parameters);
            }

            return Format(template, dict, missingPlaceholder);
        }

        public bool ContainsParam(string template, string paramName)
        {
            if (string.IsNullOrEmpty(template))
                return false;

            var placeholder = $"{_prefix}{paramName}{_suffix}";
            return template.Contains(placeholder, StringComparison.Ordinal);
        }

        public IEnumerable<string> ExtractParams(string template)
        {
            if (string.IsNullOrEmpty(template))
                return Enumerable.Empty<string>();

            return Regex.Matches(template, _pattern)
                .Select(m => m.Groups[1].Value)
                .Distinct();
        }

        public string Replace(string template, string paramName, string value)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            var placeholder = $"{_prefix}{paramName}{_suffix}";
            return template.Replace(placeholder, value);
        }
    }
}
