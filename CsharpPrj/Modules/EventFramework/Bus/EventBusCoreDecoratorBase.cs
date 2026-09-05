
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Bus;

public abstract class EventBusCoreDecoratorBase : IEventBusCore
{
    protected readonly IEventBusCore Inner;

    protected EventBusCoreDecoratorBase(IEventBusCore inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }


    public virtual void Publish(string eventName, object? data = null)
        => Inner.Publish(eventName, data);

    public virtual void Publish<TEvent>(TEvent @event) where TEvent : class
        => Inner.Publish(@event);


    public void Subscribe(string eventName, Action<object?> handler)
        => Inner.Subscribe(eventName, handler);

    public void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class
        => Inner.Subscribe(subscriber, handler);


    public void Unsubscribe(string eventName, Action<object?> handler)
        => Inner.Unsubscribe(eventName, handler);

    public void Unsubscribe<TEvent>(object subscriber) where TEvent : class
        => Inner.Unsubscribe<TEvent>(subscriber);


    public bool HasSubscribers(string eventName)
        => Inner.HasSubscribers(eventName);

    public int SubscriberCount(string eventName)
        => Inner.SubscriberCount(eventName);

    public IEnumerable<string> GetEventNames()
        => Inner.GetEventNames();


    public void UnsubscribeAll(string eventName)
        => Inner.UnsubscribeAll(eventName);

    public void ClearAllSubscriptions()
        => Inner.ClearAllSubscriptions();


    public virtual void Dispose()
    {
        Inner.Dispose();
    }
}
