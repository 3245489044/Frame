using Common.Diagnostics;

namespace Common.PlaceholderFramework.Core
{
    public static class PlaceholderLogHelper
    {
        public static void Log(string level, string category, string module, string message)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = (level?.ToUpperInvariant()) ?? "INFO",
                Category = LogCategoryResolver.Resolve(category),
                Module = module,
                Message = message
            };
            LogRepository.Write(logEntry);
        }

        public static void LogInfo(string category, string module, string message)
            => Log("INFO", category, module, message);

        public static void LogWarn(string category, string module, string message)
            => Log("WARN", category, module, message);

        public static void LogError(string category, string module, string message)
            => Log("ERROR", category, module, message);

        public static void LogDebug(string category, string module, string message)
            => Log("DEBUG", category, module, message);
    }
}
