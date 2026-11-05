
using System.Text.RegularExpressions;

namespace ParameterResolver.KeyMapping;

public static class PathProcessor
{
    public static bool IsPatternMatch(string input, string pattern, string wildcardChar)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern))
            return false;

        if (pattern == wildcardChar)
            return true;

        if (!pattern.Contains(wildcardChar))
            return input == pattern;

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\" + wildcardChar, ".*") + "$";
        return Regex.IsMatch(input, regexPattern);
    }

    public static string[] SplitKeyParts(string key, string separator)
    {
        if (string.IsNullOrEmpty(key))
            return Array.Empty<string>();

        return key.Split(new[] { separator }, StringSplitOptions.None);
    }

    public static class PathBuilder
    {
        public static string CombinePath(string basePath, string hierarchy, string separator = ".")
        {
            if (string.IsNullOrEmpty(basePath)) return hierarchy;
            if (string.IsNullOrEmpty(hierarchy)) return basePath;
            return $"{hierarchy}{separator}{basePath}";
        }
    }
}
