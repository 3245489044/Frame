
using System;

namespace Share.EventBindingOrchestrator.Diagnostics;

public enum BindingLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IEventBindingLogger
{
    void Log(BindingLogLevel level, string eventName, string message, object? data = null);

    void LogError(string eventName, string message, Exception exception);
}

public static class EventBindingLoggerExtensions
{
    public static void Info(this IEventBindingLogger logger, string eventName, string message, object? data = null)
        => logger.Log(BindingLogLevel.Info, eventName, message, data);

    public static void Warn(this IEventBindingLogger logger, string eventName, string message, object? data = null)
        => logger.Log(BindingLogLevel.Warning, eventName, message, data);

    public static void Debug(this IEventBindingLogger logger, string eventName, string message, object? data = null)
        => logger.Log(BindingLogLevel.Debug, eventName, message, data);
}