
using Modules.DiagnosticContext.Core;

namespace GraphFramework.Diagnostics;

public static class GraphFrameworkDiagnosticContext
{
    public static void Record(string eventName, object? data = null)
        => DiagnosticContextProxy.Record(eventName, data);
}
