
using System.Collections.Concurrent;
using Modules.EventFramework.Abstractions;
using TConfig;

namespace Modules.EventFramework.Bus;


public class EventBusFacade : IEventBusCore, TConfig.IEventBus
{
    private IEventBusCore _eventBus;
    private TConfig.IEventBus? _uiEventBus;
    private readonly ConcurrentDictionary<Func<object?, Task>, Action<object?>> _asyncHandlerMap = new();

    public EventBusFacade(IEventBusCore? eventBus = null, TConfig.IEventBus? uiEventBus = null)
    {
        _eventBus = eventBus ?? new DefaultEventBusCore();
        _uiEventBus = uiEventBus;
    }

    public IEventBusCore EventBus
    {
        get => _eventBus;
        set => _eventBus = value ?? throw new ArgumentNullException(nameof(value));
    }

    public TConfig.IEventBus? UiEventBus
    {
        get => _uiEventBus;
        set => _uiEventBus = value;
    }

    public Action<Exception>? OnAsyncHandlerError { get; set; }

    #region IEventBusCore — 发布事件

    public void Publish(string eventName, object? data = null)
        => _eventBus.Publish(eventName, data);

    public void Publish<TEvent>(TEvent @event) where TEvent : class
        => _eventBus.Publish(@event);

    #endregion

    #region IEventBusCore — 订阅事件

    public void Subscribe(string eventName, Action<object?> handler)
        => _eventBus.Subscribe(eventName, handler);

    private readonly object _subscribeLock = new();

    public void Subscribe(string eventName, Func<object?, Task> handler)
    {
        lock (_subscribeLock)
        {
            if (_asyncHandlerMap.TryGetValue(handler, out var existingWrapper))
            {
                _eventBus.Unsubscribe(eventName, existingWrapper);
                _asyncHandlerMap.TryRemove(handler, out _);
            }

            Action<object?> newWrapper = data =>
            {
                try
                {
                    var task = handler(data);
                    task.ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            var ex = t.Exception.InnerException ?? t.Exception;
                            OnAsyncHandlerError?.Invoke(ex);
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception ex)
                {
                    OnAsyncHandlerError?.Invoke(ex);
                }
            };

            _eventBus.Subscribe(eventName, newWrapper);
            _asyncHandlerMap[handler] = newWrapper;
        }
    }

    public void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class
    {
        _eventBus.Subscribe(subscriber, handler);
    }

    #endregion

    #region IEventBusCore — 取消订阅

    public void Unsubscribe(string eventName, Action<object?> handler)
        => _eventBus.Unsubscribe(eventName, handler);

    public void Unsubscribe(string eventName, Func<object?, Task> handler)
    {
        lock (_subscribeLock)
        {
            if (_asyncHandlerMap.TryRemove(handler, out var wrapper))
                _eventBus.Unsubscribe(eventName, wrapper);
        }
    }

    public void Unsubscribe<TEvent>(object subscriber) where TEvent : class
        => _eventBus.Unsubscribe<TEvent>(subscriber);

    #endregion

    #region IEventBusCore — 查询

    public bool HasSubscribers(string eventName)
        => _eventBus.HasSubscribers(eventName);

    public int SubscriberCount(string eventName)
        => _eventBus.SubscriberCount(eventName);

    public IEnumerable<string> GetEventNames()
        => _eventBus.GetEventNames();

    #endregion

    #region IEventBusCore — 批量操作

    public void UnsubscribeAll(string eventName)
        => _eventBus.UnsubscribeAll(eventName);

    public void ClearAllSubscriptions()
        => _eventBus.ClearAllSubscriptions();

    #endregion

    #region TConfig.IEventBus 委托 — 控件事件绑定

    public bool CanHandleControl(object control)
    {
        if (_uiEventBus != null)
            return _uiEventBus.CanHandleControl(control);

        return false;
    }






    public void BindControlEvent(object control, string eventName, Delegate handler)
    {
        if (_uiEventBus != null)
        {
            _uiEventBus.BindControlEvent(control, eventName, handler);
            return;
        }

        throw new NotSupportedException(
            $"EventBusFacade 未设置 UI 事件总线。请通过构造函数或 UiEventBus 属性设置 TConfig.IEventBus 实现。");
    }

    public void UnbindControlEvent(object control, string eventName)
    {
        if (_uiEventBus != null)
        {
            _uiEventBus.UnbindControlEvent(control, eventName);
            return;
        }

        throw new NotSupportedException(
            $"EventBusFacade 未设置 UI 事件总线。请通过构造函数或 UiEventBus 属性设置 TConfig.IEventBus 实现。");
    }

    #endregion

    #region TConfig.IEventBus — 异步适配

    TConfig.IEventSubscription TConfig.IEventBus.Subscribe(string eventName, Func<EventContext, Task> handler)
    {
        Func<object?, Task> wrappedHandler = data => handler(new EventContext { EventArgs = data });
        Subscribe(eventName, wrappedHandler);
        return new SubscriptionToken(() => Unsubscribe(eventName, wrappedHandler));
    }

    Task TConfig.IEventBus.PublishAsync(string eventName, EventContext context)
    {
        Publish(eventName, context.EventArgs);
        return Task.CompletedTask;
    }

    void TConfig.IEventBus.Clear() => ClearAll();

    #endregion

    #region 清理

    public void ClearAll()
    {
        _eventBus.ClearAllSubscriptions();
        _asyncHandlerMap.Clear();
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _asyncHandlerMap.Clear();
    }

    #endregion
}
