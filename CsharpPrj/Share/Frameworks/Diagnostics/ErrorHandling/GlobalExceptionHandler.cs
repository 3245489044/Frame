
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
                // 直接使用网关捕获到的原始异常，保留类型、StackTrace 与 InnerException（§4.7）。
                var ex = ExceptionGateway.CurrentException ?? new Exception(record.Message);

                switch (record.Kind)
                {
                    case CrashKind.ThreadException:
                        onThreadException?.Invoke(ex);
                        break;
                    case CrashKind.AppDomainUnhandled:
                    case CrashKind.UnobservedTask:
                    case CrashKind.FirstChance:
                    case CrashKind.Halt:
                        onAppDomainException?.Invoke(ex);
                        break;
                    default:
                        onAppDomainException?.Invoke(ex);
                        break;
                }
            });
        }
    }
}
