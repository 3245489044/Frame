
namespace TConfig;

public record EventMappingEntry(string EventName, EventAccessor Accessor, int Priority = 100);

public record EventAccessor(Delegate Handler, Type? EventDelegateType = null);

public interface IEventMappingRegistry
{
    void Register(string eventName, EventAccessor accessor, int priority = 100);

    void RegisterRange(IEnumerable<EventMappingEntry> entries);

    EventAccessor? Find(string eventName);

    IEnumerable<EventAccessor> FindAll(string eventName);

    void Unregister(string eventName);

    void Clear();
}
