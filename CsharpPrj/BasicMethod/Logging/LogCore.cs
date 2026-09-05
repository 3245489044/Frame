
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Common.Diagnostics
{

    public interface ILogger
    {
        string ModuleName { get; }


        void Info(string category, string message);

        void Info(string category, string format, params object?[] args);

        void Warn(string category, string message);

        void Warn(string category, string format, params object?[] args);

        void Error(string category, string message, Exception? exception = null);

        void Error(string category, string format, params object?[] args);

        void Fatal(string category, string message, Exception? exception = null);

        void Fatal(string category, string format, params object?[] args);

        void Debug(string category, string message);

        void Debug(string category, string format, params object?[] args);


        void Info(LogCategory category, string message);

        void Info(LogCategory category, string format, params object?[] args);

        void Warn(LogCategory category, string message);

        void Warn(LogCategory category, string format, params object?[] args);

        void Error(LogCategory category, string message);

        void Error(LogCategory category, string message, Exception exception);

        void Error(LogCategory category, string format, params object?[] args);

        void Fatal(LogCategory category, string message);

        void Fatal(LogCategory category, string message, Exception exception);

        void Fatal(LogCategory category, string format, params object?[] args);


        void Debug(LogCategory category, string message);

        void Debug(LogCategory category, string format, params object?[] args);


        void LogInfo(string message);

        void LogWarning(string message);

        void LogError(string message);

        void LogDebug(string message);

        void LogInfo(string format, params object[] args);

        void LogWarning(string format, params object[] args);

        void LogError(string format, params object[] args);

        void LogDebug(string format, params object[] args);
    }


    [Flags]
    public enum LogOutputTarget
    {
        Console = 1 << 0,

        Debug = 1 << 1,

        UI = 1 << 2,

        Repository = 1 << 3,

        Default = Console | Debug | Repository
    }



    public class SimpleLogger : ILogger
    {
        public string ModuleName { get; }

        public LogCategory EnabledCategories { get; set; } = LogCategory.All;

        public SimpleLogger(string moduleName)
        {
            ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
        }


        public void Info(string category, string message)
            => Write("INFO", category, message);

        public void Info(string category, string format, params object?[] args)
            => Write("INFO", category, string.Format(format, args));

        public void Warn(string category, string message)
            => Write("WARN", category, message);

        public void Warn(string category, string format, params object?[] args)
            => Write("WARN", category, string.Format(format, args));

        public void Error(string category, string message, Exception? exception = null)
            => Write("ERROR", category, exception != null ? $"{message} | {exception}" : message);

        public void Error(string category, string format, params object?[] args)
            => Write("ERROR", category, string.Format(format, args));

        public void Fatal(string category, string message, Exception? exception = null)
            => Write("FATAL", category, exception != null ? $"{message} | {exception}" : message);

        public void Fatal(string category, string format, params object?[] args)
            => Write("FATAL", category, string.Format(format, args));

        public void Debug(string category, string message)
            => Write("DEBUG", category, message);

        public void Debug(string category, string format, params object?[] args)
            => Write("DEBUG", category, string.Format(format, args));


        public void Info(LogCategory category, string message)
            => WriteCategory("INFO", category, message);

        public void Info(LogCategory category, string format, params object?[] args)
            => WriteCategory("INFO", category, string.Format(format, args));

        public void Warn(LogCategory category, string message)
            => WriteCategory("WARN", category, message);

        public void Warn(LogCategory category, string format, params object?[] args)
            => WriteCategory("WARN", category, string.Format(format, args));

        public void Error(LogCategory category, string message)
            => WriteCategory("ERROR", category, message);

        public void Error(LogCategory category, string message, Exception exception)
            => WriteCategory("ERROR", category, $"{message} | {exception}");

        public void Error(LogCategory category, string format, params object?[] args)
            => WriteCategory("ERROR", category, string.Format(format, args));

        public void Fatal(LogCategory category, string message)
            => WriteCategory("FATAL", category, message);

        public void Fatal(LogCategory category, string message, Exception exception)
            => WriteCategory("FATAL", category, $"{message} | {exception}");

        public void Fatal(LogCategory category, string format, params object?[] args)
            => WriteCategory("FATAL", category, string.Format(format, args));

        public void Debug(LogCategory category, string message)
            => WriteCategory("DEBUG", category, message);

        public void Debug(LogCategory category, string format, params object?[] args)
            => WriteCategory("DEBUG", category, string.Format(format, args));


        public void LogInfo(string message)
            => Write("INFO", "System", message);

        public void LogWarning(string message)
            => Write("WARN", "System", message);

        public void LogError(string message)
            => Write("ERROR", "System", message);

        public void LogDebug(string message)
            => Write("DEBUG", "System", message);

        public void LogInfo(string format, params object[] args)
            => Write("INFO", "System", string.Format(format, args));

        public void LogWarning(string format, params object[] args)
            => Write("WARN", "System", string.Format(format, args));

        public void LogError(string format, params object[] args)
            => Write("ERROR", "System", string.Format(format, args));

        public void LogDebug(string format, params object[] args)
            => Write("DEBUG", "System", string.Format(format, args));


        private void Write(string level, string category, string message)
        {
            LogRepository.Write(level, category, ModuleName, message);
        }

        private void WriteCategory(string level, LogCategory category, string message)
        {
            var effective = category & LoggingSetup.GlobalCategories & EnabledCategories;
            if (effective == LogCategory.None && category != LogCategory.None)
                return;

            LogRepository.Write(level, category.ToString(), ModuleName, message);
        }
    }


    public static class LoggingExtensions
    {
        public static ILogger CreateLogger(this string moduleName, Action<string>? uiLogAction = null)
        {
            return new SimpleLogger(moduleName);
        }
    }

    public static class LoggingSetup
    {
        private static volatile bool _initialized;
        private static readonly object _initLock = new();

        public static LogCategory GlobalCategories { get; private set; } = LogCategory.All;

        public static ILogger Default { get; private set; } = new SimpleLogger("Default");

        public static Action? OnBeforeInitialize { get; set; }

        public static Func<LogCategory, string>? ResolveCategoryDisplayName { get; set; }

        public static Func<string, LogCategory>? ResolveEnumName { get; set; }

        public static bool IsOutputEnabled(LogCategory category) => GlobalCategories.HasFlag(category);

        public static void Configure(LogCategory categories) => GlobalCategories = categories;

        public static void Initialize(LogCategory categories = LogCategory.None)
        {
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            OnBeforeInitialize?.Invoke();

            Configure(categories);

            Default = new SimpleLogger("Default");
        }

        public static void Reset()
        {
            LogCollapser.Reset();
            GlobalCategories = LogCategory.All;
            lock (_initLock)
            {
                _initialized = false;
            }
        }

        public static ILogger CreateLogger(string moduleName, Action<string>? uiLogAction = null)
        {
            return new SimpleLogger(moduleName);
        }

        public static ILogger CreateLogger(string moduleName, LogCategory enabledCategories, Action<string>? uiLogAction = null)
        {
            return new SimpleLogger(moduleName)
            {
                EnabledCategories = enabledCategories
            };
        }
    }

}
