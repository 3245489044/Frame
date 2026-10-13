
using System;

namespace Common.Diagnostics
{
    /// <summary>
    /// 兼容垫片：转发到唯一异常出口 ExceptionGateway（不再 Environment.Exit，保命优先）。
    /// 新代码请直接用 ExceptionGateway.Configure（全库异常订阅点收敛为 1）。
    /// </summary>
    public static class GlobalExceptionHandler
    {
        public static void Configure(
            Action<Exception>? onThreadException = null,
            Action<Exception>? onAppDomainException = null)
        {
            ExceptionGateway.Configure(onCrash: record =>
            {
                var ex = new Exception(record.Message);
                if (record.Kind == CrashKind.ThreadException)
                {
                    onThreadException?.Invoke(ex);
                }
                else
                {
                    onAppDomainException?.Invoke(ex);
                }
            });
        }
    }
}
