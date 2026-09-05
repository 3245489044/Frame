
using BackEndDriven.Diagnostics;

namespace BackEndDriven.Execution;

public static class GlobalExceptionHandler
{
    private static bool _configured;
    private static readonly object _lock = new();
    private static UnhandledExceptionEventHandler? _handler;

    public static void Configure(
        Action<Exception>? onAppDomainException = null)
    {
        if (_configured) return;

        lock (_lock)
        {
            if (_configured) return;

            _handler = (sender, e) =>
            {
                var exception = e.ExceptionObject as Exception
                    ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
                BackEndLog.Log("GlobalExceptionHandler_Unhandled", new { error = exception.Message });
                if (onAppDomainException != null)
                    onAppDomainException(exception);
            };

            AppDomain.CurrentDomain.UnhandledException += _handler;

            _configured = true;
        }
    }

    // Reset 须移除已订阅 handler，否则重复 Configure 会叠加重复订阅（重复日志）。
    internal static void Reset()
    {
        lock (_lock)
        {
            if (_handler != null)
            {
                AppDomain.CurrentDomain.UnhandledException -= _handler;
                _handler = null;
            }
            _configured = false;
        }
    }
}
