
using Modules.DiagnosticContext.Core;

namespace CommandFramework.Diagnostics;

public static class CommandFrameworkDiagnosticContext
{
    public static void Record(string eventName, object? data = null)
        => DiagnosticContextProxy.Record(eventName, data);
}
