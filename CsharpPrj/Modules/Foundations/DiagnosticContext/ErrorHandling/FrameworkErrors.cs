
using Common.Diagnostics;

namespace Modules.DiagnosticContext
{
    public enum ErrorSeverity
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public class FrameworkErrorEventArgs : EventArgs
    {
        public string Source { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public ErrorSeverity Severity { get; }

        public FrameworkErrorEventArgs(string source, string message, Exception? exception = null, ErrorSeverity severity = ErrorSeverity.Error)
        {
            Source = source;
            Message = message;
            Exception = exception;
            Severity = severity;
        }
    }

    public static class FrameworkErrors
    {
        public static event EventHandler<FrameworkErrorEventArgs>? ErrorOccurred;

        public static void Report(string source, string message, Exception? exception = null, ErrorSeverity severity = ErrorSeverity.Error)
        {
            var level = severity switch
            {
                ErrorSeverity.Debug => "Debug",
                ErrorSeverity.Info => "Info",
                ErrorSeverity.Warning => "Warn",
                ErrorSeverity.Error => "Error",
                ErrorSeverity.Critical => "Fatal",
                _ => "Error"
            };

            LogRepository.Write(level, LogCategory.System.ToString(), source,
                exception != null
                    ? $"{message} | Exception: {exception.Message}"
                    : message);

            ErrorOccurred?.Invoke(null, new FrameworkErrorEventArgs(source, message, exception, severity));
        }

        public static string GetErrorMessage(ulong errorCode)
        {
            return errorCode switch
            {
                0x0001 => "未知错误",
                0x0002 => "操作超时",
                0x0003 => "参数无效",
                0x0004 => "资源未找到",
                0x0005 => "权限不足",

                0x0101 => "日志写入失败",
                0x0102 => "日志存储空间不足",
                0x0103 => "日志分类未注册",
                0x0104 => "日志查询失败",

                0x0201 => "管道初始化失败",
                0x0202 => "管道执行超时",
                0x0203 => "管道步骤执行失败",

                0x0301 => "配置文件未找到",
                0x0302 => "配置文件格式错误",
                0x0303 => "配置项缺失",

                0x0401 => "数据库连接失败",
                0x0402 => "数据库查询失败",
                0x0403 => "数据库写入失败",

                _ => $"未知错误码: 0x{errorCode:X4}"
            };
        }

        public static LogCategory GetErrorCategory(ulong errorCode)
        {
            return errorCode switch
            {
                >= 0x0001 and <= 0x0005 => LogCategory.System,

                >= 0x0101 and <= 0x0104 => LogCategoryRegistry.ResolveEnumName("FileDriven_Diagnostic"),

                >= 0x0201 and <= 0x0203 => LogCategoryRegistry.ResolveEnumName("WdUI_Pipeline"),

                >= 0x0301 and <= 0x0303 => LogCategory.Configuration,

                >= 0x0401 and <= 0x0403 => LogCategory.Database,

                _ => LogCategory.System
            };
        }

        public static string GetErrorLevel(ulong errorCode)
        {
            return errorCode switch
            {
                0x0001 => "Fatal",
                0x0101 => "Fatal",
                0x0201 => "Fatal",
                0x0401 => "Fatal",

                0x0002 => "Error",
                0x0102 => "Error",
                0x0202 => "Error",
                0x0301 => "Error",
                0x0402 => "Error",

                0x0003 => "Warning",
                0x0103 => "Warning",
                0x0203 => "Warning",
                0x0302 => "Warning",
                0x0403 => "Warning",

                0x0004 => "Info",
                0x0005 => "Info",
                0x0303 => "Info",
                0x0104 => "Info",

                _ => "Unknown"
            };
        }

    }
}
