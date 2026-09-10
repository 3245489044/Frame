
using System.Diagnostics;
using FrameworkAbstractions.ComponentRegistry;

namespace FrameworkAbstractions.ErrorHandling;

public class DefaultErrorReporter : IErrorReporter
{
    public void Report(string source, string message, Exception? exception = null, ErrorSeverity severity = ErrorSeverity.Error)
    {
        var severityLabel = severity switch
        {
            ErrorSeverity.Debug => "DEBUG",
            ErrorSeverity.Info => "INFO",
            ErrorSeverity.Warning => "WARN",
            ErrorSeverity.Error => "ERROR",
            ErrorSeverity.Critical => "CRITICAL",
            _ => "ERROR"
        };

        var logMessage = $"[{severityLabel}] [{source}] {message}";
        if (exception != null)
        {
            logMessage += $"\nException: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
        }

        FrameworkAbstractions.Diagnostics.FrameworkAbstractionsDiagnosticContext.Record("error.report", new { severity = severityLabel, source, message, exception = exception?.Message });

        if (severity >= ErrorSeverity.Error)
        {
            Console.Error.WriteLine(logMessage);
        }
    }

    public static void Register()
    {
        AppServiceProvider.Register<IErrorReporter>(new DefaultErrorReporter());
    }
}
