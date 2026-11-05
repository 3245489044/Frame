
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Bus;

public enum EventScopePolicy
{
    Exact,

    CurrentAndChildren,

    CurrentAndParents,

    All
}

public class ScopedEvent
{
    public string ScopeId { get; }

    public string EventName { get; }

    public object? Data { get; }

    public DateTime Timestamp { get; }

    public bool IsPropagated { get; }

    public ScopedEvent(string scopeId, string eventName, object? data, bool isPropagated = false)
    {
        ScopeId = scopeId ?? throw new ArgumentNullException(nameof(scopeId));
        EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        Data = data;
        Timestamp = DateTime.UtcNow;
        IsPropagated = isPropagated;
    }
}

public class PropagationEvent : ScopedEvent
{
    public string SourceScopeId { get; }

    public bool IsCancelled { get; private set; }

    public PropagationEvent(string scopeId, string eventName, object? data, bool isPropagated = false)
        : base(scopeId, eventName, data, isPropagated)
    {
        SourceScopeId = scopeId;
    }

    public void Cancel() => IsCancelled = true;
}

public class EventScope : IDisposable
{
    private readonly IEventBusCore _bus;
    private readonly EventScopePolicy _policy;
    private readonly List<EventScope> _children = new();
    private EventScope? _parent;
    private readonly object _lock = new();
    private bool _disposed;

    public string ScopeId { get; }

    public IEventBusCore Bus => _bus;

    public EventScope? Parent => _parent;

    public IReadOnlyList<EventScope> Children
    {
        get { lock (_lock) return _children.ToList(); }
    }

    public EventScope(string scopeId, IEventBusCore bus, EventScopePolicy policy = EventScopePolicy.Exact)
    {
        ScopeId = scopeId ?? throw new ArgumentNullException(nameof(scopeId));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _policy = policy;
    }

    public EventScope CreateChildScope(string scopeId)
    {
        if (scopeId == null) throw new ArgumentNullException(nameof(scopeId));
        var child = new EventScope(scopeId, _bus, _policy);
        AddChild(child);
        return child;
    }

    public void AddChild(EventScope child)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));
        lock (_lock)
        {
            child._parent = this;
            _children.Add(child);
        }
    }

    public void RemoveChild(EventScope child)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));
        lock (_lock)
        {
            child._parent = null;
            _children.Remove(child);
        }
    }

    public void PublishAndPropagate(string eventName, object? data)
    {
        if (_disposed) return;

        var scopedEvent = new ScopedEvent(ScopeId, eventName, data, isPropagated: false);
        _bus.Publish(eventName, scopedEvent);

        _parent?.PropagateFromChild(eventName, data, ScopeId);
    }

    private void PropagateFromChild(string eventName, object? data, string sourceScopeId)
    {
        PropagateFromChild(eventName, data, sourceScopeId, new HashSet<string>());
    }

    private void PropagateFromChild(string eventName, object? data, string sourceScopeId, HashSet<string> visited)
    {
        if (_disposed) return;

        if (!visited.Add(ScopeId))
            return;

        var propagationEvent = new PropagationEvent(sourceScopeId, eventName, data, isPropagated: true);
        _bus.Publish(eventName, propagationEvent);

        _parent?.PropagateFromChild(eventName, data, sourceScopeId, visited);
    }

    public SubscriptionToken Subscribe(string eventName, Action<ScopedEvent> handler, EventScopePolicy policy = EventScopePolicy.Exact)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        Action<object?> wrappedHandler = data =>
        {
            if (data is ScopedEvent scopedEvent && ShouldReceive(scopedEvent.ScopeId))
            {
                handler(scopedEvent);
            }
        };

        _bus.Subscribe(eventName, wrappedHandler);

        return new SubscriptionToken(() => _bus.Unsubscribe(eventName, wrappedHandler));
    }

    public bool ShouldReceive(string sourceScopeId)
    {
        lock (_lock)
        {
            if (_disposed) return false;
        }

        return _policy switch
        {
            EventScopePolicy.Exact => ScopeId == sourceScopeId,
            EventScopePolicy.CurrentAndChildren => ScopeId == sourceScopeId || IsChildScope(sourceScopeId),
            EventScopePolicy.CurrentAndParents => ScopeId == sourceScopeId || IsParentScope(sourceScopeId),
            EventScopePolicy.All => true,
            _ => false
        };
    }

    private bool IsChildScope(string scopeId)
    {
        lock (_lock)
        {
            foreach (var child in _children)
            {
                if (child.ScopeId == scopeId) return true;
                if (child.IsChildScope(scopeId)) return true;
            }
        }
        return false;
    }

    private bool IsParentScope(string scopeId)
    {
        var current = _parent;
        while (current != null)
        {
            if (current.ScopeId == scopeId) return true;
            current = current._parent;
        }
        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    foreach (var child in _children)
                        child.Dispose();
                    _children.Clear();
                }
            }
            _bus.Dispose();
        }
    }
}
