
using TConfig;

namespace Modules.EventFramework.Abstractions;

public interface IEventBusCore : IDisposable
{
    void Publish(string eventName, object? data = null);

    void Publish<TEvent>(TEvent @event) where TEvent : class;

    void Subscribe(string eventName, Action<object?> handler);

    void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class;

    void Unsubscribe(string eventName, Action<object?> handler);

    void Unsubscribe<TEvent>(object subscriber) where TEvent : class;

    bool HasSubscribers(string eventName);

    void UnsubscribeAll(string eventName);

    int SubscriberCount(string eventName);

    IEnumerable<string> GetEventNames();

    void ClearAllSubscriptions();
}
public interface IEventMappingRegistry
{
    void RegisterMapping(string eventName, EventAccessor accessor, int priority = 100);

    void RegisterMappings(IEnumerable<EventMappingEntry> entries);

    EventAccessor? GetAccessor(string eventName);

    IEnumerable<string> GetRegisteredEvents();

    bool UnregisterMapping(string eventName);

    void Clear();
}
public interface IEventNameNormalizer
{
    void RegisterMapping(string lowerCaseName, string pascalCaseName);

    void RegisterMappings(IEnumerable<KeyValuePair<string, string>> mappings);

    string ToPascalCase(string eventName);

    IReadOnlyDictionary<string, string> GetKnownMappings();

    bool UnregisterMapping(string lowerCaseName);
}
public interface IEventSource : IDisposable
{
    string Id { get; }

    string Name { get; }

    string SourceType { get; }

    bool CanSubscribe { get; }

    bool Subscribe(string eventName, Delegate handler);

    bool Unsubscribe(string eventName, Delegate? handler = null);

    IEnumerable<string> GetSupportedEvents();
}
public interface IEventSourceNode : IEventSource, IValueContainer, ITreeNode
{

    new IEnumerable<IEventSourceNode> GetChildren();

    new IEventSourceNode? FindChild(string name);
}

public interface IParameterBuilder<TBinding>
{
    Dictionary<string, object?> Build(
        object sender,
        IReadOnlyList<TBinding>? bindings,
        Func<object, string, object?>? valueResolver = null);
}
