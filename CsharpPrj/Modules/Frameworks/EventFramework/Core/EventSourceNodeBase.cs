
using System.Collections.Concurrent;
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Core;

public abstract class EventSourceNodeBase : IEventSourceNode
{
    private readonly ConcurrentDictionary<string, List<Delegate>> _subscriptions = new();

    public string Id { get; }

    public abstract string Name { get; }

    public abstract string SourceType { get; }

    public bool IsInitialized { get; private set; }

    public bool IsDisposed { get; private set; }


    public virtual bool CanSubscribe => true;

    public virtual bool Subscribe(string eventName, Delegate handler)
    {
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var list = _subscriptions.GetOrAdd(eventName, _ => new List<Delegate>());
        lock (list)
        {
            list.Add(handler);
        }
        return OnSubscribe(eventName, handler);
    }

    public virtual bool Unsubscribe(string eventName, Delegate? handler = null)
    {
        if (handler == null)
        {
            _subscriptions.TryRemove(eventName, out _);
            return OnUnsubscribeAll(eventName);
        }

        if (_subscriptions.TryGetValue(eventName, out var list))
        {
            lock (list)
            {
                list.Remove(handler);
            }
        }
        return OnUnsubscribe(eventName, handler);
    }

    public virtual IEnumerable<string> GetSupportedEvents() => _subscriptions.Keys;


    public virtual bool CanRead => false;

    public virtual bool CanWrite => false;

    public virtual object? GetValue() => throw new NotSupportedException($"'{GetType().Name}' does not support reading value.");

    public virtual void SetValue(object? value) => throw new NotSupportedException($"'{GetType().Name}' does not support writing value.");


    public virtual bool HasChildren => false;

    public IEnumerable<IEventSourceNode> GetChildren() => GetChildrenCore();

    public IEventSourceNode? FindChild(string name) => FindChildCore(name);

    IEnumerable<ITreeNode> ITreeNode.GetChildren() => GetChildrenCore().Cast<ITreeNode>();

    ITreeNode? ITreeNode.FindChild(string name) => FindChildCore(name);

    protected virtual IEnumerable<IEventSourceNode> GetChildrenCore() => Enumerable.Empty<IEventSourceNode>();

    protected virtual IEventSourceNode? FindChildCore(string name) => null;


    public virtual void Initialize()
    {
        if (IsInitialized) return;
        IsInitialized = true;
        OnInitialize();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        OnDispose();
        _subscriptions.Clear();
    }


    protected abstract bool OnSubscribe(string eventName, Delegate handler);

    protected abstract bool OnUnsubscribe(string eventName, Delegate handler);

    protected virtual bool OnUnsubscribeAll(string eventName) => true;

    protected virtual void OnInitialize() { }

    protected virtual void OnDispose() { }

    protected EventSourceNodeBase()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    protected EventSourceNodeBase(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }
}
