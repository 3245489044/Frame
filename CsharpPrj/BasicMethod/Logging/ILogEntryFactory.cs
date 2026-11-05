
using System.Runtime.CompilerServices;

namespace Common.Diagnostics
{
    public interface ILogEntryFactory
    {
        LogEntry CreateEntry(
            string level,
            LogCategory category,
            string module,
            string message,
            Exception? exception,
            string? callerFile,
            int callerLine,
            string? callerMember);
    }

    public class DefaultEntryFactory : ILogEntryFactory
    {
        public static readonly DefaultEntryFactory Instance = new();

        public LogEntry CreateEntry(
            string level,
            LogCategory category,
            string module,
            string message,
            Exception? exception,
            string? callerFile,
            int callerLine,
            string? callerMember)
        {
            return new LogEntry
            {
                // 时间唯一来源：LoggingSetup.Clock（UTC 存储 → 本地显示单点转换，禁散落本地时钟直读）。
                Timestamp = LoggingSetup.Clock.UtcNow.LocalDateTime,
                Level = level,
                Category = category,
                Module = module,
                Message = message,
                SourceFile = !string.IsNullOrEmpty(callerFile) ? Path.GetFileName(callerFile) : null,
                SourceLine = callerLine > 0 ? callerLine : null,
                SourceMember = callerMember,
                Exception = exception
            };
        }
    }
}
