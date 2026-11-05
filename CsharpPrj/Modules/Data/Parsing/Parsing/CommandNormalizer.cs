using System.Collections.Concurrent;

namespace Modules.Parsing.Parsing;

public static class CommandNormalizer
{
    private static readonly HashSet<string> ActionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "load", "set", "click", "echo", "close", "show", "list", "sleep", "validate",
        "log"
    };

    private static readonly ConcurrentDictionary<string, string> NormalizationMap = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterActionCommand(string command)
    {
        ActionCommands.Add(command);
    }

    public static void RegisterActionCommands(IEnumerable<string> commands)
    {
        foreach (var cmd in commands)
            ActionCommands.Add(cmd);
    }

    public static void RegisterNormalization(string shorthand, string fullCommand)
    {
        NormalizationMap[shorthand] = fullCommand;
    }

    public static void RegisterNormalizations(IEnumerable<KeyValuePair<string, string>> mappings)
    {
        foreach (var kvp in mappings)
            NormalizationMap[kvp.Key] = kvp.Value;
    }

    public static string Normalize(string command)
    {
        if (ShouldSkip(command))
            return string.Empty;

        var trimmed = command.TrimStart();

        var firstWord = CommandTokenizerExtensions.GetFirstWord(trimmed);

        if (firstWord != null && NormalizationMap.TryGetValue(firstWord, out var normalizedPrefix))
        {
            var rest = trimmed.Substring(firstWord.Length).TrimStart();
            return string.IsNullOrEmpty(rest)
                ? normalizedPrefix
                : $"{normalizedPrefix} {rest}";
        }

        if (firstWord != null && ActionCommands.Contains(firstWord))
        {
            return "do " + trimmed;
        }

        return command;
    }

    public static bool ShouldSkip(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var trimmed = line.TrimStart();
        return string.IsNullOrWhiteSpace(trimmed)
            || trimmed.StartsWith("#")
            || trimmed.StartsWith("//");
    }

    [Obsolete("Use ShouldSkip instead")]
    public static bool IsComment(string command) => ShouldSkip(command);
}
