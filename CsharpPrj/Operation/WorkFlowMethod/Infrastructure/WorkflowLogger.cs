#region USINGS
using System.Runtime.CompilerServices;
using WorkFlowMethod.Diagnostics;
#endregion

namespace WorkFlowMethod.Infrastructure
{
    /// <summary>
    /// 异常处理静态入口 —— 统一通过 <see cref="WorkFlowMethodLog"/> 输出，不再依赖 IExecutionLogger / DebugWorkflowLogger。
    /// </summary>
    public static class ExceptionHandler
    {
        public static void LogAndSwallow(string context, Exception ex,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var fileName = Path.GetFileName(filePath);
            var message = $"[{fileName}:{lineNumber}] {context} 发生异常: {ex.Message}";
            WorkFlowMethodLog.Error("ExceptionHandler", message, ex,
                new { File = fileName, Line = lineNumber, Context = context, Message = ex.Message });
        }

        public static async Task<T> FallbackAsync<T>(Func<Task<T>> operation,
            T fallbackValue, string context,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                LogAndSwallow(context, ex, filePath, lineNumber);
                return fallbackValue;
            }
        }

        public static T Fallback<T>(Func<T> operation, T fallbackValue, string context,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                LogAndSwallow(context, ex, filePath, lineNumber);
                return fallbackValue;
            }
        }

        public static async Task FallbackAsync(Func<Task> operation, string context,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                LogAndSwallow(context, ex, filePath, lineNumber);
            }
        }
    }
}