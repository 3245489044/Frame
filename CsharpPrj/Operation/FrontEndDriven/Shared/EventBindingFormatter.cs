using TConfig;

namespace FrontEndDriven.Shared;

public static class EventBindingFormatter
{
    public static List<string> Format(EventConfig evt)
    {
        var names = new List<string>();

        var processedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var eventType in EventHelper.StandardEventTypes)
        {
            var funcs = EventHelper.GetEventFunctions(evt, eventType);
            if (funcs is { Count: > 0 })
            {
                names.Add($"{EventHelper.ToPascalCase(eventType)}=[{string.Join(",", funcs)}]");
                processedTypes.Add(eventType);
            }
        }

        var customEvents = evt.EventsDictionary?
            .Where(kv => !processedTypes.Contains(kv.Key) && kv.Value is { Count: > 0 })
            .ToList();
        if (customEvents is { Count: > 0 })
            names.Add($"Custom=[{string.Join(",", customEvents.Select(kv => $"{kv.Key}=[{string.Join(",", kv.Value)}]"))}]");

        return names;
    }
}