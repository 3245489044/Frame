
using Modules.DiagnosticContext.Core;

namespace Share.EventBindingOrchestrator.Diagnostics;

public static class EventBindingDiagnosticContext
{
    public static IDisposable BeginScope()
    {
        return new DiagnosticScope("EventBinding");
    }

    public static void Record(string eventName, object? data = null)
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        if (scope == null)
        {
            Console.Error.WriteLine($"[EventBinding] {eventName}: {data}");
            return;
        }
        scope.Record(eventName, data);
    }

    public static object CaptureSnapshot()
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope?.Freeze() ?? new object();
    }

    public static string GetSnapshotString()
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope?.ToJson() ?? "{}";
    }
}
