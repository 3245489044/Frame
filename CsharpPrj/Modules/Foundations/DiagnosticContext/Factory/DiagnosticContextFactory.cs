
namespace Modules.DiagnosticContext.Factory;

public static class DiagnosticContextFactory
{
    public static Core.DiagnosticContext Create(string scopeName)
    {
        var scope = new Core.DiagnosticScope(scopeName);
        return new Core.DiagnosticContext(scope);
    }

    internal static Core.DiagnosticContext CreateLazy(string scopeName)
    {
        var scope = new Core.DiagnosticScope(scopeName);
        return new Core.DiagnosticContext(scope);
    }
}
