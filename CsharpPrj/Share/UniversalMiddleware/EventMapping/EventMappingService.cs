
#nullable enable
using TConfig;

namespace UniversalMiddleware.EventMapping;

public class EventMappingService : IEventMappingService
{
    private readonly Dictionary<string, Func<EventConfig, List<string>?>> _eventPropertyAccessors = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _controlTypeToEventType = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] s_standardEventPriority = {
        "click", "textchanged", "selectedindexchanged", "doubleclick",
        "checkedchanged", "valuechanged"
    };

    public EventMappingService()
    {
        RegisterDefaultAccessors();
        RegisterDefaultControlTypeMappings();
    }

    private void RegisterDefaultAccessors()
    {
        _eventPropertyAccessors["click"] = events => GetFromDictionary(events, "click");
        _eventPropertyAccessors["selectedindexchanged"] = events => GetFromDictionary(events, "selectedindexchanged");
        _eventPropertyAccessors["textchanged"] = events => GetFromDictionary(events, "textchanged");
        _eventPropertyAccessors["doubleclick"] = events => GetFromDictionary(events, "doubleclick");
        _eventPropertyAccessors["checkedchanged"] = events => GetFromDictionary(events, "checkedchanged");
        _eventPropertyAccessors["valuechanged"] = events => GetFromDictionary(events, "valuechanged");
    }

    private static List<string>? GetFromDictionary(EventConfig events, string eventKey)
    {
        if (events.EventsDictionary != null &&
            events.EventsDictionary.TryGetValue(eventKey, out var funcList) &&
            funcList is { Count: > 0 })
        {
            return funcList;
        }
        return null;
    }

    private void RegisterDefaultControlTypeMappings()
    {
        _controlTypeToEventType["combobox"] = "selectedindexchanged";
        _controlTypeToEventType["textbox"] = "textchanged";
        _controlTypeToEventType["checkbox"] = "checkedchanged";
        _controlTypeToEventType["numericupdown"] = "valuechanged";
        _controlTypeToEventType["trackbar"] = "valuechanged";
    }


    private IEnumerable<(string EventType, string FuncName)> EnumerateAllEvents(EventConfig events)
    {
        if (events == null)
            yield break;

        foreach (var eventType in s_standardEventPriority)
        {
            if (_eventPropertyAccessors.TryGetValue(eventType, out var getter))
            {
                var funcNames = getter(events);
                if (funcNames is { Count: > 0 })
                {
                    foreach (var funcName in funcNames)
                    {
                        if (!string.IsNullOrEmpty(funcName))
                            yield return (eventType, funcName);
                    }
                }
            }
        }

        if (events.EventsDictionary is { Count: > 0 })
        {
            var standardSet = new HashSet<string>(s_standardEventPriority, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in events.EventsDictionary)
            {
                if (!standardSet.Contains(kvp.Key) && kvp.Value is { Count: > 0 })
                {
                    foreach (var funcName in kvp.Value)
                    {
                        if (!string.IsNullOrEmpty(funcName))
                            yield return (kvp.Key, funcName);
                    }
                }
            }
        }
    }


    public List<string>? GetEventFunctions(EventConfig events, string eventType)
    {
        if (events == null || string.IsNullOrWhiteSpace(eventType))
            return null;

        if (_eventPropertyAccessors.TryGetValue(eventType, out var accessor))
        {
            var funcList = accessor(events);
            return funcList != null && funcList.Count > 0 ? funcList : null;
        }

        if (events.EventsDictionary != null &&
            events.EventsDictionary.TryGetValue(eventType, out var customFuncs) &&
            customFuncs is { Count: > 0 })
        {
            return customFuncs;
        }

        return null;
    }

    public string? GetEventFunctionName(EventConfig eventsConfig, string eventName)
    {
        var funcList = GetEventFunctions(eventsConfig, eventName);
        return funcList?.FirstOrDefault();
    }

    public string? GetFirstNonEmptyEventFunctionName(EventConfig eventsConfig)
    {
        return EnumerateAllEvents(eventsConfig).Select(e => e.FuncName).FirstOrDefault();
    }

    public string InferEventType(string controlType, string operationType)
    {
        if (!string.Equals(operationType, "set", StringComparison.OrdinalIgnoreCase))
            return operationType;

        if (string.IsNullOrEmpty(controlType))
            return "textchanged";

        if (_controlTypeToEventType.TryGetValue(controlType, out var eventType))
            return eventType;

        return "textchanged";
    }

    public bool IsValidEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return false;

        return _eventPropertyAccessors.ContainsKey(eventType);
    }

    public IEnumerable<string> GetSupportedEventTypes()
    {
        return _eventPropertyAccessors.Keys;
    }

    public string[] GetBoundEventNames(EventConfig events)
    {
        return EnumerateAllEvents(events).Select(e => e.EventType).Distinct().ToArray();
    }

    public void ForEachStandardEvent(EventConfig events, Action<string, string> callback)
    {
        if (events == null) return;

        var standardEvents = new HashSet<string>(s_standardEventPriority);
        foreach (var (eventType, funcName) in EnumerateAllEvents(events))
        {
            if (standardEvents.Contains(eventType))
                callback(eventType, funcName);
        }
    }

    public void ForEachCustomEvent(EventConfig events, Action<string, string> callback)
    {
        if (events == null) return;

        var standardEvents = new HashSet<string>(s_standardEventPriority);
        foreach (var (eventType, funcName) in EnumerateAllEvents(events))
        {
            if (!standardEvents.Contains(eventType))
                callback(eventType, funcName);
        }
    }

    public void ForEachBoundEvent(EventConfig events, Action<string, string> callback)
    {
        foreach (var (eventType, funcName) in EnumerateAllEvents(events))
        {
            callback(eventType, funcName);
        }
    }

    public void RegisterEventAccessor(string eventType, Func<EventConfig, List<string>?> accessor)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be null or empty", nameof(eventType));
        if (accessor == null)
            throw new ArgumentNullException(nameof(accessor));

        _eventPropertyAccessors[eventType] = accessor;
    }
}
