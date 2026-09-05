
using Modules.DiagnosticContext.Core;

namespace FrameworkAbstractions.Diagnostics;

public static class FrameworkAbstractionsDiagnosticContext
{
    public static void Record(string eventName, object? data = null)
        => DiagnosticContextProxy.Record(eventName, data);
}
