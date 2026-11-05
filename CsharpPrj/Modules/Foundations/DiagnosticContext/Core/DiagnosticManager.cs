
using Modules.DiagnosticContext.Abstractions;

namespace Modules.DiagnosticContext.Core;

public static class DiagnosticManager
{
    public static DiagnosticContext? Current => DiagnosticContext.Current;

    public static DiagnosticContext? FirstActive
    {
        get
        {
            var allScopes = DiagnosticScopeStore.GetAllActive();
            var first = allScopes.FirstOrDefault();
            return first != null ? new DiagnosticContext(first) : null;
        }
    }

    public static void RecordToAll(string eventName, object? data = null)
    {
        foreach (var scope in DiagnosticScopeStore.GetAllActive())
        {
            scope.Record(eventName, data);
        }
    }

    public static DiagnosticContext EnsureScope(string scopeName = "Auto")
    {
        return DiagnosticContext.GetOrCreateLazy(scopeName);
    }

    public static string? TryGetSnapshotJson()
    {
        return DiagnosticContext.TryGetSnapshotJson();
    }

    public static DiagnosticContext BeginScope(string scopeName)
    {
        var scope = new DiagnosticScope(scopeName);
        return new DiagnosticContext(scope);
    }

    public static string GetSnapshot()
    {
        var allScopes = DiagnosticScopeStore.GetAllActive();
        if (allScopes.Count == 0)
            return "(no active contexts)";

        var sb = new System.Text.StringBuilder();
        int index = 0;
        foreach (var scope in allScopes)
        {
            if (index > 0) sb.AppendLine();
            sb.Append(scope.ToString());
            index++;
        }
        return sb.ToString();
    }
}
