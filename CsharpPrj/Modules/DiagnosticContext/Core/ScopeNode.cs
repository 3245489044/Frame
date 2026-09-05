
using Modules.DiagnosticContext.Formatting;

namespace Modules.DiagnosticContext.Core;

public class ScopeNode
{
    private readonly List<EventEntry> _events = new();
    private readonly Dictionary<string, object?> _state = new();
    private readonly List<ScopeNode> _children = new();
    private readonly DateTime _createdAt;
    private readonly IReadOnlyList<EventEntry> _eventsReadOnly;
    private readonly IReadOnlyList<ScopeNode> _childrenReadOnly;
    private bool _disposed;

    public string Name { get; }

    public DateTime CreatedAt => _createdAt;

    public IReadOnlyList<EventEntry> Events => _eventsReadOnly;

    public IReadOnlyDictionary<string, object?> State => _state;

    public IReadOnlyList<ScopeNode> Children => _childrenReadOnly;

    public ScopeNode? Parent { get; }

    public ScopeNode(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _createdAt = DateTime.UtcNow;
        Parent = null;
        _eventsReadOnly = _events.AsReadOnly();
        _childrenReadOnly = _children.AsReadOnly();
    }

    private ScopeNode(string name, ScopeNode parent)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Parent = parent;
        _createdAt = DateTime.UtcNow;
        _eventsReadOnly = _events.AsReadOnly();
        _childrenReadOnly = _children.AsReadOnly();
    }

    public ScopeNode Root
    {
        get
        {
            var node = this;
            while (node.Parent != null)
                node = node.Parent;
            return node;
        }
    }

    public void Record(string eventName, object? data = null)
    {
        if (_disposed) return;
        var relativeMs = (long)(DateTime.UtcNow - Root._createdAt).TotalMilliseconds;
        _events.Add(new EventEntry(eventName, relativeMs, data));
    }

    internal void AddEntry(EventEntry entry)
    {
        if (_disposed) return;
        _events.Add(entry);
    }

    public void SetState(string key, object? value)
    {
        if (_disposed) return;
        _state[key] = value;
    }

    public T? GetState<T>(string key)
    {
        if (_state.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public ScopeNode CreateScope(string scopeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ScopeNode));

        var child = new ScopeNode(scopeName, this);
        _children.Add(child);
        return child;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var child in _children)
            child.Dispose();
    }

    public override string ToString() => TextFormatter.Default.Format(this);

    public string ToSummary() => SummaryFormatter.Default.Format(this);

    public string ToJson() => JsonFormatter.Default.Format(this);
}
