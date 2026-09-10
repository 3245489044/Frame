
using CommandFramework.Abstractions;

namespace CommandFramework.Libraries;

public static class NonEventCommands
{

    public const string Load = "load";

    public const string Close = "close";

    public const string Echo = "echo";

    public const string Sleep = "sleep";

    public const string Show = "show";

    public const string Look = "look";

    public const string Wait = "wait";

    public const string Assert = "assert";

    public const string Discover = "discover";

    public const string List = "list";

    public const string Validate = "validate";

    public const string Exit = "exit";

    public const string Sideload = "sideload";

    public const string Exec = "exec";


    private static readonly Dictionary<string, CommandCriticality> _commandCriticalityMap = new(
        StringComparer.OrdinalIgnoreCase)
    {
        { Load,    CommandCriticality.Critical },
        { Close,   CommandCriticality.Important },
        { Echo,    CommandCriticality.Important },
        { Sleep,   CommandCriticality.Important },
        { Assert,  CommandCriticality.Important },
        { Sideload,CommandCriticality.Important },
        { Exec,    CommandCriticality.Important },
        { Show,    CommandCriticality.Optional },
        { Look,    CommandCriticality.Optional },
        { Wait,    CommandCriticality.Optional },
        { Discover,CommandCriticality.Optional },
        { List,    CommandCriticality.Optional },
        { Validate,CommandCriticality.Optional },
        { Exit,    CommandCriticality.Optional },
    };

    public static readonly IReadOnlyCollection<string> AllCommands =
        new List<string>(_commandCriticalityMap.Keys).AsReadOnly();

    private static readonly ILookup<CommandCriticality, string> _commandsByCriticality =
        _commandCriticalityMap.ToLookup(kvp => kvp.Value, kvp => kvp.Key);

    public static IReadOnlyCollection<string> CriticalCommands =>
        _commandsByCriticality[CommandCriticality.Critical].ToList().AsReadOnly();

    public static IReadOnlyCollection<string> ImportantCommands =>
        _commandsByCriticality[CommandCriticality.Important].ToList().AsReadOnly();

    public static IReadOnlyCollection<string> OptionalCommands =>
        _commandsByCriticality[CommandCriticality.Optional].ToList().AsReadOnly();

    public static bool IsNonEventCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        return _commandCriticalityMap.ContainsKey(commandName);
    }

    public static CommandCriticality GetCriticality(string commandName)
    {
        if (_commandCriticalityMap.TryGetValue(commandName, out var criticality))
            return criticality;
        return CommandCriticality.Optional;
    }
}
