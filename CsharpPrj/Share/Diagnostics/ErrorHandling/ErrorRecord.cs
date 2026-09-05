
using System;

namespace Common.Diagnostics
{
    public class ErrorRecord
    {
        public string ErrorId { get; } = Guid.NewGuid().ToString("N");

        public string? SourceProject { get; set; }

        public string? SourceModule { get; set; }

        public string? SourceClass { get; set; }

        public string? SourceMethod { get; set; }

        public string Message { get; set; } = string.Empty;

        public string? ExceptionType { get; set; }

        public string? StackTrace { get; set; }

        public string? InnerErrorMessage { get; set; }

        public string? InnerExceptionType { get; set; }

        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public static ErrorRecord FromException(Exception ex, string? sourceModule = null)
        {
            var record = new ErrorRecord
            {
                SourceProject = ProjectSourceResolver.ResolveFromStackTrace(ex),
                SourceModule = sourceModule,
                Message = ex.Message,
                ExceptionType = ex.GetType().FullName,
                StackTrace = ex.ToString(),
                InnerErrorMessage = ex.InnerException?.Message,
                InnerExceptionType = ex.InnerException?.GetType().FullName
            };

            if (ex.StackTrace != null)
            {
                var frames = ex.StackTrace.Split('\n');
                foreach (var frame in frames)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        frame.Trim(),
                        @"at\s+([^\s(]+)\.([^\s.(]+)\(([^)]*)\)");
                    if (match.Success)
                    {
                        var fullTypeName = match.Groups[1].Value;
                        var methodName = match.Groups[2].Value;

                        if (!fullTypeName.StartsWith("System.") &&
                            !fullTypeName.StartsWith("Microsoft."))
                        {
                            record.SourceClass = fullTypeName;
                            record.SourceMethod = methodName;
                            break;
                        }
                    }
                }
            }

            return record;
        }

        public object ToLogParameters()
        {
            return new
            {
                errorId = ErrorId,
                sourceProject = SourceProject,
                sourceModule = SourceModule,
                sourceClass = SourceClass,
                sourceMethod = SourceMethod,
                errorMessage = Message,
                exceptionType = ExceptionType,
                stackTrace = StackTrace,
                innerErrorMessage = InnerErrorMessage,
                innerExceptionType = InnerExceptionType,
                timestamp = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };
        }

        public string ToShortString()
        {
            var project = SourceProject ?? "Unknown";
            var module = SourceModule ?? "";
            var location = SourceClass ?? "";
            if (SourceMethod != null) location += $".{SourceMethod}";
            return $"[{project}{(module.Length > 0 ? $"/{module}" : "")}] {Message}";
        }
    }
}
