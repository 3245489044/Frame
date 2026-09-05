
using System.Collections.Concurrent;
using TConfig;

namespace Modules.EventFramework.Bus;

public class DefaultEventBus : TConfig.IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<TConfig.EventContext, Task>>> _asyncHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Action<object?>>> _syncHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();


    public void Publish<TEvent>(TEvent @event) where TEvent : class
        => throw new NotSupportedException("DefaultEventBus 不支持强类型事件。请使用命名事件或 IEventBusCore。");

    public void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class
        => throw new NotSupportedException("DefaultEventBus 不支持强类型事件。请使用命名事件或 IEventBusCore。");

    public void Unsubscribe<TEvent>(object subscriber) where TEvent : class
        => throw new NotSupportedException("DefaultEventBus 不支持强类型事件。请使用命名事件或 IEventBusCore。");


    public void Publish(string eventName, object? args)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        List<Action<object?>> handlers;
        lock (_lock)
        {
            if (!_syncHandlers.TryGetValue(eventName, out handlers))
                return;
            handlers = handlers.ToList();
        }

        foreach (var handler in handlers)
        {
            handler(args);
        }
    }

    public void Subscribe(string eventName, Action<object?> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            if (!_syncHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers = new List<Action<object?>>();
                _syncHandlers[eventName] = handlers;
            }
            handlers.Add(handler);
        }
    }

    public void Unsubscribe(string eventName, Action<object?> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        lock (_lock)
        {
            if (_syncHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                    _syncHandlers.TryRemove(eventName, out _);
            }
        }
    }


    public TConfig.IEventSubscription Subscribe(string eventName, Func<TConfig.EventContext, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            if (!_asyncHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers = new List<Func<TConfig.EventContext, Task>>();
                _asyncHandlers[eventName] = handlers;
            }
            handlers.Add(handler);
        }

        return new SubscriptionToken(() =>
        {
            lock (_lock)
            {
                if (_asyncHandlers.TryGetValue(eventName, out var h))
                {
                    h.Remove(handler);
                    if (h.Count == 0)
                        _asyncHandlers.TryRemove(eventName, out _);
                }
            }
        });
    }

    public async Task PublishAsync(string eventName, TConfig.EventContext context)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        List<Func<TConfig.EventContext, Task>> handlers;
        lock (_lock)
        {
            if (!_asyncHandlers.TryGetValue(eventName, out handlers))
                return;
            handlers = handlers.ToList();
        }

        var tasks = handlers.Select(h => h(context));
        await Task.WhenAll(tasks);
    }

    public bool HasSubscribers(string eventName)
    {
        lock (_lock)
        {
            return _asyncHandlers.ContainsKey(eventName) || _syncHandlers.ContainsKey(eventName);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _asyncHandlers.Clear();
            _syncHandlers.Clear();
        }
    }


    public bool CanHandleControl(object control) => false;

    public void BindControlEvent(object control, string eventName, Delegate handler)
        => throw new NotSupportedException("DefaultEventBus 不支持控件事件绑定。请使用 WinFormsEventBus 或 EventBusFacade。");

    public void UnbindControlEvent(object control, string eventName)
        => throw new NotSupportedException("DefaultEventBus 不支持控件事件绑定。请使用 WinFormsEventBus 或 EventBusFacade。");
}
