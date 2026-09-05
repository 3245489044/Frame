
#nullable enable

using System.Text.RegularExpressions;

namespace Modules.GraphProcessing.Normalization
{
    public class IdentifierExtractor
    {
        private readonly string _separator;
        private readonly bool _preserveCase;

        public IdentifierExtractor(string separator = "_", bool preserveCase = false)
        {
            _separator = separator;
            _preserveCase = preserveCase;
        }

        public string Extract(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var cleaned = Regex.Replace(input, $@"[^a-zA-Z0-9{Regex.Escape(_separator)}]", _separator);

            cleaned = Regex.Replace(cleaned, @"(?<=[a-z])(?=[A-Z])", _separator);
            cleaned = Regex.Replace(cleaned, @"(?<=[A-Z])(?=[A-Z][a-z])", _separator);

            var parts = cleaned.Split(new[] { _separator }, StringSplitOptions.RemoveEmptyEntries);

            if (!_preserveCase)
            {
                parts = Array.ConvertAll(parts, p => p.ToLowerInvariant());
            }

            return string.Join(_separator, parts);
        }

        public string ExtractUniqueKey(string input, params string[] additionalParts)
        {
            var baseKey = Extract(input);
            if (additionalParts.Length == 0)
                return baseKey;

            var allParts = new List<string> { baseKey };
            foreach (var part in additionalParts)
            {
                var extracted = Extract(part);
                if (!string.IsNullOrEmpty(extracted))
                    allParts.Add(extracted);
            }

            return string.Join(_separator, allParts.Where(p => !string.IsNullOrEmpty(p)));
        }

        public bool AreMatching(string a, string b)
        {
            return string.Equals(Extract(a), Extract(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
