
using System.Collections.Concurrent;

namespace Modules.DiagnosticContext.Core;

public static class DiagnosticScopeStore
{
    private static readonly AsyncLocal<string?> _currentCallChainId = new();

    private static readonly ConcurrentDictionary<string, DiagnosticScope> _scopes = new();

    public static string? CurrentCallChainId
    {
        get => _currentCallChainId.Value;
        set => _currentCallChainId.Value = value;
    }

    internal static void Register(string id, DiagnosticScope scope)
        => _scopes[id] = scope;

    internal static void Unregister(string id)
        => _scopes.TryRemove(id, out _);

    public static DiagnosticScope? GetCurrent()
    {
        var id = _currentCallChainId.Value;
        if (id == null) return null;
        return _scopes.TryGetValue(id, out var scope) ? scope : null;
    }

    public static DiagnosticScope? GetById(string callChainId)
        => _scopes.TryGetValue(callChainId, out var scope) ? scope : null;

    public static IReadOnlyCollection<DiagnosticScope> GetAllActive()
        => _scopes.Values.ToList().AsReadOnly();

    public static int ActiveCount => _scopes.Count;
}
