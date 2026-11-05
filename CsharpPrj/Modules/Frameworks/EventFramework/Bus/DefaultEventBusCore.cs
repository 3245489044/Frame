
using System.Collections.Concurrent;
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Bus;

public class DefaultEventBusCore : IEventBusCore
{
    protected readonly object _lock = new();
    protected readonly Dictionary<string, List<Action<object?>>> _eventHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, Dictionary<object, Delegate>> _typedEventHandlers = new();
    private bool _disposed;

    public virtual void Publish(string eventName, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        List<Action<object?>>? handlers;
        lock (_lock)
        {
            if (!_eventHandlers.TryGetValue(eventName, out handlers))
                return;
            handlers = handlers.ToList();
        }

        foreach (var handler in handlers)
            handler(data);
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        var eventType = typeof(TEvent);
        Dictionary<object, Delegate>? handlers;

        lock (_lock)
        {
            if (!_typedEventHandlers.TryGetValue(eventType, out handlers))
                return;
            handlers = new Dictionary<object, Delegate>(handlers);
        }

        foreach (var handler in handlers.Values)
        {
            if (handler is Action<TEvent> typedHandler)
                typedHandler(@event);
        }
    }

    public void Subscribe(string eventName, Action<object?> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("事件名称不能为空", nameof(eventName));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            if (!_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers = new List<Action<object?>>();
                _eventHandlers[eventName] = handlers;
            }
            handlers.Add(handler);
        }
    }

    public void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class
    {
        if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var eventType = typeof(TEvent);
        lock (_lock)
        {
            if (!_typedEventHandlers.TryGetValue(eventType, out var handlers))
            {
                handlers = new Dictionary<object, Delegate>();
                _typedEventHandlers[eventType] = handlers;
            }
            handlers[subscriber] = handler;
        }
    }

    public void Unsubscribe(string eventName, Action<object?> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("事件名称不能为空", nameof(eventName));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                    _eventHandlers.Remove(eventName);
            }
        }
    }

    public void Unsubscribe<TEvent>(object subscriber) where TEvent : class
    {
        if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));

        var eventType = typeof(TEvent);
        lock (_lock)
        {
            if (_typedEventHandlers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(subscriber);
                if (handlers.Count == 0)
                    _typedEventHandlers.Remove(eventType);
            }
        }
    }

    public bool HasSubscribers(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        lock (_lock)
        {
            return _eventHandlers.ContainsKey(eventName);
        }
    }

    public void UnsubscribeAll(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        lock (_lock)
        {
            _eventHandlers.Remove(eventName);
        }
    }

    public int SubscriberCount(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        lock (_lock)
        {
            return _eventHandlers.TryGetValue(eventName, out var handlers)
                ? handlers.Count
                : 0;
        }
    }

    public IEnumerable<string> GetEventNames()
    {
        lock (_lock)
        {
            return _eventHandlers.Keys.ToList();
        }
    }

    public void ClearAllSubscriptions()
    {
        lock (_lock)
        {
            _eventHandlers.Clear();
            _typedEventHandlers.Clear();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _eventHandlers.Clear();
                    _typedEventHandlers.Clear();
                    _disposed = true;
                }
            }
        }
    }
}
