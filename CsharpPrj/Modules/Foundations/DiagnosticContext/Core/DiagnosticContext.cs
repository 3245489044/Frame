
using Modules.DiagnosticContext.Abstractions;
using Modules.DiagnosticContext.Factory;

namespace Modules.DiagnosticContext.Core;

public sealed class DiagnosticContext : IDiagnosticContext
{
    private readonly DiagnosticScope _scope;
    private bool _disposed;


    public static DiagnosticContext? Current
    {
        get
        {
            var scope = DiagnosticScopeStore.GetCurrent();
            if (scope == null) return null;
            return scope.GetOrCreateWrapper();
        }
    }


    public string Name => _scope.RootNode.Name;

    public DateTime CreatedAt => _scope.RootNode.CreatedAt;

    internal ScopeNode CurrentScope => _scope.CurrentNode;

    public ScopeNode RootScope => _scope.RootNode;


    internal DiagnosticContext(DiagnosticScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }


    public void Record(string eventName, object? data = null)
    {
        if (_disposed) return;
        _scope.Record(eventName, data);
    }

    public void SetState(string key, object? value)
    {
        if (_disposed) return;
        _scope.SetState(key, value);
    }

    public T? GetState<T>(string key) => _scope.CurrentNode.GetState<T>(key);

    public IDiagnosticContext CreateScope(string scopeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DiagnosticContext));
        var popper = _scope.PushScope(scopeName);
        return new ScopeGuard(this, _scope.CurrentNode, popper);
    }

    public DiagnosticSnapshot Freeze() => _scope.Freeze();

    public override string ToString() => _scope.ToString();

    public string ToSummary() => _scope.ToSummary();

    public string ToJson() => _scope.ToJson();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }


    internal static DiagnosticContext GetOrCreateLazy(string scopeName = "Lazy")
    {
        var current = DiagnosticScopeStore.GetCurrent();
        if (current != null)
            return current.GetOrCreateWrapper();

        var scope = new DiagnosticScope(scopeName);
        return scope.GetOrCreateWrapper();
    }

    internal static string? TryGetSnapshotJson()
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope?.ToJson();
    }


    public sealed class ScopeGuard : IDiagnosticContext
    {
        private readonly DiagnosticContext _owner;
        private readonly ScopeNode _scopeNode;
        private readonly IDisposable _popper;
        private bool _disposed;

        internal ScopeGuard(DiagnosticContext owner, ScopeNode scopeNode, IDisposable popper)
        {
            _owner = owner;
            _scopeNode = scopeNode;
            _popper = popper;
        }

        public string Name => _scopeNode.Name;
        public DateTime CreatedAt => _scopeNode.CreatedAt;

        public void Record(string eventName, object? data = null)
        {
            if (_disposed) return;
            _scopeNode.Record(eventName, data);
        }

        public void SetState(string key, object? value)
        {
            if (_disposed) return;
            _scopeNode.SetState(key, value);
        }

        public T? GetState<T>(string key) => _scopeNode.GetState<T>(key);

        public IDiagnosticContext CreateScope(string scopeName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScopeGuard));
            return _owner.CreateScope(scopeName);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _popper.Dispose();
        }

        public override string ToString() => _scopeNode.ToString();
        public string ToSummary() => _scopeNode.ToSummary();
        public string ToJson() => _scopeNode.ToJson();
    }
}
