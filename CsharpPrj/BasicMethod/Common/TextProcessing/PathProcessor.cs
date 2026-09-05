namespace Common.GraphMindStudio
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Linq;

    public static class PathProcessor
    {
        private const string LABEL_KEY = "label";
        public const char DEFAULT_SEPARATOR = '.';
        public const string DEFAULT_SEPARATOR_STRING = ".";

        public static string PrefabsRootPath { get; set; } = "Prefabs";
        public static string UIPrefabsPath { get; set; } = "Prefabs/UIPrefebs/";

        public static string ExtractBaseKeyFromNumberedSuffix(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return fullKey;
            int dotIndex = fullKey.LastIndexOf(DEFAULT_SEPARATOR);
            return dotIndex > 0 && int.TryParse(fullKey.Substring(dotIndex + 1), out _) ? fullKey.Substring(0, dotIndex) : fullKey;
        }

        public static bool IsPatternMatch(string value, string pattern, string wildcardChar)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern)) return false;
            return pattern.EndsWith("*") ? value.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                                         : string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsKeyMatch(string key, string pattern)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(pattern)) return false;
            return pattern.EndsWith("*") ? key.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                                         : string.Equals(key, pattern, StringComparison.OrdinalIgnoreCase);
        }

        public static string[] SplitKeyParts(string key, string separator = ".")
        {
            return string.IsNullOrEmpty(key) ? Array.Empty<string>() : key.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string BuildKeyBySeparator(string[] targetLevels, string[] sourceLevels, Dictionary<int, int> levelMatches, string separator)
        {
            var resultLevels = new string[targetLevels.Length];
            Array.Copy(targetLevels, resultLevels, targetLevels.Length);
            foreach (var match in levelMatches)
                if (match.Key < resultLevels.Length && match.Value < sourceLevels.Length)
                    resultLevels[match.Key] = sourceLevels[match.Value];
            return string.Join(separator, resultLevels);
        }

        public static class PathBuilder
        {
            public static string CombinePath(string key, string hierarchy)
            {
                if (string.IsNullOrEmpty(key)) return hierarchy;
                if (string.IsNullOrEmpty(hierarchy)) return key;
                return key + DEFAULT_SEPARATOR_STRING + hierarchy;
            }
        }
    }
}
