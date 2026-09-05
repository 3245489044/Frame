
using System;
using System.Windows.Forms;

namespace Common.Diagnostics
{
    public static class GlobalExceptionHandler
    {
        public static void Configure(
            Action<Exception>? onThreadException = null,
            Action<Exception>? onAppDomainException = null)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, e) =>
            {
                onThreadException?.Invoke(e.Exception);
                Environment.Exit(1);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception
                    ?? new Exception(e.ExceptionObject.ToString());
                onAppDomainException?.Invoke(ex);
                Environment.Exit(1);
            };
        }
    }
}
