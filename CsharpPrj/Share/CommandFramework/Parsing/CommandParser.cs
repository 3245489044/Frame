
using CommandFramework.Abstractions;
using CommandFramework.Models;
using Modules.Registry;

namespace CommandFramework.Parsing;

public class CommandParser : ICommandParser
{
    public static IReadOnlyCollection<string> DefaultNonEventCommands =>
        CommandFramework.Libraries.NonEventCommands.AllCommands;

    private readonly ISetRegistry<string> _nonEventCommands;
    private readonly IMapRegistry<string, string> _normalizationMap;

    public CommandParser(
        ISetRegistry<string>? nonEventCommands = null,
        IMapRegistry<string, string>? normalizationMap = null)
    {
        _nonEventCommands = nonEventCommands ?? CreateDefaultNonEventCommands();
        _normalizationMap = normalizationMap ?? new NormalizationRegistry(includeDefaults: true);
    }

    private static SetRegistryBase<string> CreateDefaultNonEventCommands()
    {
        var registry = new SetRegistryBase<string>(StringComparer.OrdinalIgnoreCase);
        registry.RegisterRange(DefaultNonEventCommands);
        return registry;
    }

    public ParsedCommand Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new ParsedCommand { IsSuccess = false, ErrorMessage = "Command is required" };

        try
        {
            var normalized = Normalize(command);

            if (normalized == null)
            {
                return new ParsedCommand
                {
                    IsSuccess = true,
                    IsNonEventCommand = false,
                    IsComment = true,
                    NormalizedCommand = null
                };
            }

            var parts = SplitCommand(normalized);
            if (parts.Count == 0)
                return new ParsedCommand { IsSuccess = false, ErrorMessage = $"Failed to parse command: {normalized}" };

            var verb = parts[0].ToLowerInvariant();
            string? action = null;
            string? target = null;
            Dictionary<string, object>? parameters = null;

            if (parts.Count >= 2)
            {
                if (verb == "do")
                {
                    action = parts[1].ToLowerInvariant();
                    target = parts.Count >= 3 ? parts[2] : null;
                    if (parts.Count > 3)
                    {
                        parameters = ParseParameters(parts.Skip(3));
                    }
                }
                else
                {
                    action = null;
                    target = parts[1];
                    if (parts.Count > 2)
                    {
                        parameters = ParseParameters(parts.Skip(2));
                    }
                }
            }

            var isNonEvent = parts.Count == 1
                ? _nonEventCommands.Contains(verb)
                : IsNonEventCommand(verb, action);

            return new ParsedCommand
            {
                Verb = verb,
                Action = action,
                Target = target,
                Parameters = parameters,
                NormalizedCommand = normalized,
                IsNonEventCommand = isNonEvent,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            return new ParsedCommand { IsSuccess = false, ErrorMessage = $"Parse error: {ex.Message}" };
        }
    }

    public string? Normalize(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var trimmed = command.Trim();

        if (trimmed.StartsWith("#") || trimmed.StartsWith("//"))
            return null;

        if (_normalizationMap.TryGet(trimmed, out var fullCommand))
            return fullCommand;

        if (!trimmed.Contains(' ') && !IsKnownVerb(trimmed))
            return "do " + trimmed;

        return trimmed;
    }

    private static bool IsKnownVerb(string word)
    {
        var lower = word.ToLowerInvariant();
        return lower is "do" or "look" or "wait" or "get" or "assert" or "load" or "close" or "echo";
    }

    public bool IsNonEventCommand(string verb, string? action)
    {
        if (string.IsNullOrEmpty(verb))
            return false;

        if (string.Equals(verb, "do", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(action) && _nonEventCommands.Contains(action);
        }

        return _nonEventCommands.Contains(verb);
    }

    public void RegisterNormalization(string shorthand, string fullCommand)
    {
        _normalizationMap.Register(shorthand, fullCommand);
    }

    public void RegisterNonEventCommand(string commandName)
    {
        _nonEventCommands.Register(commandName);
    }

    public JsonCommand ParseAsJsonCommand(string text)
    {
        var parsed = Parse(text);
        if (!parsed.IsSuccess)
        {
            throw new ArgumentException($"Command parse failed: {parsed.ErrorMessage}", nameof(text));
        }

        return JsonCommand.FromParsedCommand(parsed, text);
    }

    public JsonCommand? TryParseAsJsonCommand(string text)
    {
        try
        {
            return ParseAsJsonCommand(text);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> SplitCommand(string command)
    {
        return StringUtils.SplitCommand(command);
    }

    private static Dictionary<string, object> ParseParameters(IEnumerable<string> paramParts)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in paramParts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            var eqIndex = part.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = part[..eqIndex].Trim();
                var value = part[(eqIndex + 1)..].Trim();

                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = string.IsNullOrEmpty(value) ? "true" : value;
                }
            }
            else
            {
                result[part.Trim()] = "true";
            }
        }

        return result;
    }
}