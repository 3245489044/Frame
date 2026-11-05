
namespace Modules.DiagnosticContext.Core;

public static class DiagnosticContextProxy
{
    public static void Record(string eventName, object? data = null)
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        scope?.Record(eventName, data);
    }
}
