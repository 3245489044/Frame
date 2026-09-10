
using Modules.DiagnosticContext.Core;

namespace UniversalMiddleware.Diagnostics;

public static class UniversalMiddlewareDiagnosticContext
{
    public static void Record(string eventName, object? data = null)
        => DiagnosticContextProxy.Record(eventName, data);
}
