#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 类型化事件总线（通用框架）：以事件类型（string）为键的多订阅者总线。
/// 统一订阅存储、快照分发、异常隔离（单个订阅者异常不中断其它订阅者）与生命周期。
/// 收敛 EventPlatform 的订阅存储 + 生命周期，EventPlatform 改为本总线的 UI 领域实例。
/// </summary>
/// <typeparam name="TEvent">事件类型。</typeparam>
public class EventBus<TEvent>
{
    private readonly Func<TEvent, string> _eventTypeSelector;
    private readonly Action<TEvent, Exception>? _handlerError;
    private readonly ConcurrentDictionary<string, List<Func<TEvent, Task>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <param name="eventTypeSelector">从事件解析订阅键（事件类型名）。</param>
    /// <param name="handlerError">订阅者执行抛异常时的回调（默认 null = 吞掉且不中断其它订阅者）。</param>
    public EventBus(Func<TEvent, string> eventTypeSelector, Action<TEvent, Exception>? handlerError = null)
    {
        _eventTypeSelector = eventTypeSelector ?? throw new ArgumentNullException(nameof(eventTypeSelector));
        _handlerError = handlerError;
    }

    /// <summary>订阅事件（Action 包装为异步）。返回可取消订阅的句柄。</summary>
    public IDisposable Subscribe(string eventType, Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeAsync(eventType, evt => { handler(evt); return Task.CompletedTask; });
    }

    /// <summary>订阅事件（异步）。返回可取消订阅的句柄。</summary>
    public IDisposable SubscribeAsync(string eventType, Func<TEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();

        var list = _handlers.GetOrAdd(eventType, _ => new List<Func<TEvent, Task>>());
        lock (list)
        {
            list.Add(handler);
        }
        return new Subscription(this, eventType, handler);
    }

    /// <summary>按派生类型订阅（仅投递 <typeparamref name="TDerived"/> 实例）。</summary>
    public IDisposable SubscribeOfType<TDerived>(string eventType, Action<TDerived> handler)
        where TDerived : class, TEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe(eventType, evt => { if (evt is TDerived typed) handler(typed); });
    }

    /// <summary>发布事件：对订阅者做快照后逐个分发；单个订阅者异常经 <paramref name="_handlerError"/> 记录，不中断其它订阅者。</summary>
    public async Task PublishAsync(TEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ThrowIfDisposed();

        if (!_handlers.TryGetValue(_eventTypeSelector(evt), out var list))
            return;

        Func<TEvent, Task>[] snapshot;
        lock (list)
        {
            snapshot = list.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(evt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _handlerError?.Invoke(evt, ex);
            }
        }
    }

    public void Publish(TEvent evt)
        => PublishAsync(evt).GetAwaiter().GetResult();

    public int SubscriberCount(string eventType)
        => _handlers.TryGetValue(eventType, out var list) ? list.Count : 0;

    public bool HasSubscribers(string eventType)
        => SubscriberCount(eventType) > 0;

    public IEnumerable<string> GetEventTypes()
        => _handlers.Keys;

    private void Unsubscribe(string eventType, Func<TEvent, Task> handler)
    {
        if (!_handlers.TryGetValue(eventType, out var list))
            return;

        lock (list)
        {
            list.Remove(handler);
            if (list.Count == 0)
                _handlers.TryRemove(eventType, out _);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventBus<TEvent>));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _handlers.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly EventBus<TEvent> _bus;
        private readonly string _eventType;
        private readonly Func<TEvent, Task> _handler;
        private bool _disposed;

        public Subscription(EventBus<TEvent> bus, string eventType, Func<TEvent, Task> handler)
        {
            _bus = bus;
            _eventType = eventType;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _bus.Unsubscribe(_eventType, _handler);
            _disposed = true;
        }
    }
}
