
using Modules.DiagnosticContext.Core;

namespace Share.StateManagement.Diagnostics;

public static class StateManagementDiagnosticContext
{
    public static void Record(string eventName, object? data = null)
        => DiagnosticContextProxy.Record(eventName, data);
}
