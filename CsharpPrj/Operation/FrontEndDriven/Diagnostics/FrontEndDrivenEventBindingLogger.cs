
using System;
using System.Collections.Generic;
using System.Reflection;
using Share.EventBindingOrchestrator.Diagnostics;

namespace FrontEndDriven.Diagnostics
{
    public sealed class FrontEndDrivenEventBindingLogger : IEventBindingLogger
    {
        private static Dictionary<string, object?> FlattenData(object? data)
        {
            var result = new Dictionary<string, object?>();
            if (data == null) return result;
            foreach (var prop in data.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    result[prop.Name] = prop.GetValue(data);
                }
                catch (Exception ex)
                {
                    // E8 修复：空 catch 显式化——属性读取失败仅记日志并跳过（不阻断其余属性扁平化）
                    FrontEndDrivenLog.Log("FrontEndDrivenEventBindingLogger_FlattenData_skip",
                        new { property = prop.Name, error = ex.Message });
                }
            }
            return result;
        }

        public void Log(BindingLogLevel level, string eventName, string message, object? data = null)
        {
            var combinedData = new Dictionary<string, object?>
            {
                ["level"] = level.ToString(),
                ["message"] = message
            };
            foreach (var kvp in FlattenData(data))
                combinedData[kvp.Key] = kvp.Value;

            FrontEndDrivenLog.Log(eventName, combinedData);

            EventBindingDiagnosticContext.Record(eventName, combinedData);
        }

        public void LogError(string eventName, string message, Exception exception)
        {
            var fullMessage = $"{message}\n异常: {exception.GetType().Name}: {exception.Message}";

            var errorData = new
            {
                level = "Error",
                message = fullMessage,
                exceptionType = exception.GetType().Name,
                exceptionMessage = exception.Message,
                stackTrace = exception.StackTrace
            };

            FrontEndDrivenLog.Log(eventName, errorData);

            EventBindingDiagnosticContext.Record(eventName, errorData);
        }

        public static readonly IEventBindingLogger Default = new FrontEndDrivenEventBindingLogger();

        private FrontEndDrivenEventBindingLogger() { }
    }
}