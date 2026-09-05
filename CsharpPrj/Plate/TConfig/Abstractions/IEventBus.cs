
namespace TConfig;

public interface IEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class;

    void Unsubscribe<TEvent>(object subscriber) where TEvent : class;

    void Publish(string eventName, object? args);

    void Subscribe(string eventName, Action<object?> handler);

    void Unsubscribe(string eventName, Action<object?> handler);

    IEventSubscription Subscribe(string eventName, Func<EventContext, Task> handler);

    Task PublishAsync(string eventName, EventContext context);

    void Clear();

    bool CanHandleControl(object control);

    void BindControlEvent(object control, string eventName, Delegate handler);

    void UnbindControlEvent(object control, string eventName);
}

public interface IEventRouter
{
    void RegisterRoute(string eventName, Func<object?, object?, bool> routeAction, int priority = 0);

    void UnregisterRoute(string eventName, Func<object?, object?, bool> routeAction);

    bool Route(string eventName, object? sender, object? args);
}
