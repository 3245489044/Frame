
#nullable enable
using System.Collections.Concurrent;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Container;

public class ContainerEventManager : IDisposable
{
    private readonly IContainer _container;
    protected readonly ConcurrentDictionary<string, List<Func<object?, Task>>> _eventHandlers = new(StringComparer.OrdinalIgnoreCase);
    protected readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();
    private bool _disposed;


    public Action<object?, ContainerConfigLoadedEventArgs>? RaiseConfigLoaded { get; set; }

    public Action<object?, EventArgs>? RaiseContentChanged { get; set; }

    public Func<IEnumerable<IContainer>>? GetSubContainers { get; set; }


    public ContainerEventManager(IContainer container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }


    public Action Subscribe(string eventName, Func<object?, Task> handler)
    {
        lock (_lock)
        {
            var handlers = _eventHandlers.GetOrAdd(eventName, _ => new List<Func<object?, Task>>());
            handlers.Add(handler);
            return () => Unsubscribe(eventName, handler);
        }
    }

    public void Unsubscribe(string eventName, Func<object?, Task> handler)
    {
        lock (_lock)
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                    _eventHandlers.TryRemove(eventName, out _);
            }
        }
    }

    public async Task PublishAsync(string eventName, object? data = null)
    {
        List<Func<object?, Task>>? handlers;
        lock (_lock)
        {
            if (!_eventHandlers.TryGetValue(eventName, out handlers))
                return;
            handlers = new List<Func<object?, Task>>(handlers);
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(data);
            }
            catch (Exception ex)
            {
                UniversalMiddleware.Diagnostics.UniversalMiddlewareDiagnosticContext.Record("container.handler.failed", new { eventName, error = ex.Message });
            }
        }
    }

    public bool HasSubscribers(string eventName)
    {
        lock (_lock)
        {
            return _eventHandlers.TryGetValue(eventName, out var handlers) && handlers.Count > 0;
        }
    }

    public IEnumerable<string> GetRegisteredEvents() => _eventHandlers.Keys.ToArray();


    public void FinalizeRender(object config)
    {
        RaiseConfigLoaded?.Invoke(_container,
            new ContainerConfigLoadedEventArgs("(render)", true, loadedConfig: config));
        RaiseContentChanged?.Invoke(_container, EventArgs.Empty);
    }


    public void RegisterSubContainerEvents(
        IContainer subContainer,
        Action<IContainer> subscribeNative)
    {
        subscribeNative(subContainer);
    }

    public void UnregisterSubContainerEvents(
        IContainer subContainer,
        Action<IContainer> unsubscribeNative)
    {
        unsubscribeNative(subContainer);
    }


    public void ClearResources(Action unsubscribeAllNative)
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        unsubscribeAllNative();
    }

    public void AddSubscription(IDisposable subscription)
    {
        _subscriptions.Add(subscription);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClearResources(() => { });
        _eventHandlers.Clear();
    }
}
