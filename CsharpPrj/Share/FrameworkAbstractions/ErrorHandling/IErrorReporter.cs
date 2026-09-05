
namespace FrameworkAbstractions.ErrorHandling;

public enum ErrorSeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public interface IErrorReporter
{
    void Report(string source, string message, Exception? exception = null, ErrorSeverity severity = ErrorSeverity.Error);
}
