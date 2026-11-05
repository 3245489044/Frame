
using Modules.DiagnosticContext.Core;

namespace Modules.DiagnosticContext.Core;

public sealed class DiagnosticScope : IDisposable
{
    private readonly ScopeNode _rootNode;
    private readonly Stack<ScopeNode> _scopeStack = new();
    private DiagnosticContext? _wrapperCache;
    private readonly string? _parentCallChainId;
    private bool _disposed;

    public string CallChainId { get; }

    public ScopeNode RootNode => _rootNode;

    public ScopeNode CurrentNode => _scopeStack.Count > 0 ? _scopeStack.Peek() : _rootNode;

    public DiagnosticScope(string scopeName)
    {
        CallChainId = $"{scopeName}#{Guid.NewGuid():N}";
        _parentCallChainId = DiagnosticScopeStore.CurrentCallChainId;
        _rootNode = new ScopeNode(scopeName);
        _scopeStack.Push(_rootNode);
        DiagnosticScopeStore.Register(CallChainId, this);
        DiagnosticScopeStore.CurrentCallChainId = CallChainId;
    }

    public void Record(string eventName, object? data = null)
    {
        if (_disposed) return;
        CurrentNode.Record(eventName, data);
    }

    public void SetState(string key, object? value)
    {
        if (_disposed) return;
        CurrentNode.SetState(key, value);
    }

    public IDisposable PushScope(string scopeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DiagnosticScope));
        var child = CurrentNode.CreateScope(scopeName);
        _scopeStack.Push(child);
        return new ScopePopper(this, child);
    }

    public string ToJson() => _rootNode.ToJson();

    public override string ToString() => _rootNode.ToString();

    public string ToSummary() => _rootNode.ToSummary();

    internal DiagnosticContext GetOrCreateWrapper()
    {
        if (_wrapperCache == null)
        {
            _wrapperCache = new DiagnosticContext(this);
        }
        return _wrapperCache;
    }

    public DiagnosticSnapshot Freeze()
    {
        return DiagnosticSnapshot.FromScope(_rootNode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DiagnosticScopeStore.Unregister(CallChainId);
        if (DiagnosticScopeStore.CurrentCallChainId == CallChainId)
            DiagnosticScopeStore.CurrentCallChainId = _parentCallChainId;
        _rootNode.Dispose();
        _scopeStack.Clear();
    }

    private sealed class ScopePopper : IDisposable
    {
        private readonly DiagnosticScope _scope;
        private readonly ScopeNode _expectedNode;
        private bool _popped;
        public ScopePopper(DiagnosticScope scope, ScopeNode expectedNode)
        {
            _scope = scope;
            _expectedNode = expectedNode;
        }
        public void Dispose()
        {
            if (_popped) return;
            _popped = true;
            if (_scope._scopeStack.Count > 1)
            {
                var top = _scope._scopeStack.Peek();
                if (top != _expectedNode)
                {
                    return;
                }
                _scope._scopeStack.Pop();
            }
        }
    }
}
