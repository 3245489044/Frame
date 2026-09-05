
#nullable enable
using TConfig;

namespace UniversalMiddleware.EventMapping;

public interface IEventMappingService
{
    List<string>? GetEventFunctions(EventConfig events, string eventType);

    string? GetEventFunctionName(EventConfig eventsConfig, string eventName);

    string? GetFirstNonEmptyEventFunctionName(EventConfig eventsConfig);

    string InferEventType(string controlType, string operationType);

    bool IsValidEventType(string? eventType);

    IEnumerable<string> GetSupportedEventTypes();

    string[] GetBoundEventNames(EventConfig events);

    void ForEachStandardEvent(EventConfig events, Action<string, string> callback);

    void ForEachCustomEvent(EventConfig events, Action<string, string> callback);

    void ForEachBoundEvent(EventConfig events, Action<string, string> callback);

    void RegisterEventAccessor(string eventType, Func<EventConfig, List<string>?> accessor);
}
