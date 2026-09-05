
using CommandFramework.Abstractions;
using TConfig;
using UniversalMiddleware;
using UniversalMiddleware.EventMapping;

namespace Share.EventBindingOrchestrator.Mapping;

public class EventTypeMapper : IEventTypeMapper
{
    public static readonly IReadOnlyDictionary<string, string> DefaultEventTypeToCommandAction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["textchanged"]          = "set",
        ["checkedchanged"]       = "set",
        ["selectedindexchanged"] = "set",
        ["valuechanged"]         = "set",
        ["click"]                = "click",
        ["doubleclick"]          = "click",
    };

    public static readonly IReadOnlyDictionary<string, string> DefaultCommandActionToDefaultEventType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["set"]   = "textchanged",
        ["click"] = "click",
    };

    public static readonly EventTypeMapper Default = new();

    private static UniversalMiddleware.EventMapping.IEventMappingService MappingService =>
        UniversalMiddlewareEntry.Instance.EventMapping;

    public IReadOnlyDictionary<string, string> EventTypeToCommandAction => DefaultEventTypeToCommandAction;

    public IReadOnlyDictionary<string, string> CommandActionToDefaultEventType => DefaultCommandActionToDefaultEventType;

    public string ToCommandAction(string eventType)
    {
        if (DefaultEventTypeToCommandAction.TryGetValue(eventType, out var action))
            return action;
        return $"do {eventType}";
    }

    public string ToDefaultEventType(string commandAction)
    {
        if (DefaultCommandActionToDefaultEventType.TryGetValue(commandAction, out var eventType))
            return eventType;
        return commandAction;
    }


    public static string? GetEventFunctionName(EventConfig eventsConfig, string eventName)
        => MappingService.GetEventFunctionName(eventsConfig, eventName);

    public static string? GetFirstNonEmptyEventFunctionName(EventConfig eventsConfig)
        => MappingService.GetFirstNonEmptyEventFunctionName(eventsConfig);
}

