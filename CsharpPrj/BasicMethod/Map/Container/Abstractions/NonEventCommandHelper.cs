
namespace Container.Abstractions;

public static class NonEventCommandHelper
{
    private static readonly HashSet<string> _knownEventCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "click"
    };

    public static bool IsNonEventCommand(string commandName, bool? lookupContains)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        if (lookupContains.HasValue)
            return lookupContains.Value;

        return !_knownEventCommands.Contains(commandName);
    }
}
