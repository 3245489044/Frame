
using Modules.DiagnosticContext.Core;

namespace Common.PlaceholderFramework.Diagnostics;

public static class PlaceholderFrameworkDiagnosticContext
{
    public static void Record(string eventName, object? data = null)
        => DiagnosticContextProxy.Record(eventName, data);
}
