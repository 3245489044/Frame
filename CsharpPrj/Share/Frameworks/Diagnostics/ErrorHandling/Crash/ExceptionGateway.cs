using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Common.Diagnostics
{
    /// <summary>
    /// 唯一异常出口（聚拢点）：集中四类订阅（UI 线程 / AppDomain / 未观察 Task / 诊断态首次机会）。
    /// 记录取证 + 发停驻信号；绝不 Environment.Exit（保命优先，异常只中断后续步骤）。
    /// 全库异常订阅点应各 ≤ 1。
    /// </summary>
    public static class ExceptionGateway
    {
        private static readonly object _lock = new();

        private static bool _configured;

        private static ThreadExceptionEventHandler? _threadHandler;

        private static UnhandledExceptionEventHandler? _appDomainHandler;

        private static EventHandler<UnobservedTaskExceptionEventArgs>? _taskHandler;

        public static Action<CrashRecord>? OnCrash { get; set; }

        public static void Configure(Action<CrashRecord>? onCrash = null, bool diagnostics = false)
        {
            lock (_lock)
            {
                if (onCrash != null) OnCrash = onCrash;
                if (_configured) return;

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                _threadHandler = (_, e) => Capture(CrashKind.ThreadException, e.Exception, "UIThread");
                Application.ThreadException += _threadHandler;

                _appDomainHandler = (_, e) =>
                {
                    var captured = e.ExceptionObject as Exception;
                    if (captured == null)
                    {
                        captured = new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
                    }
                    Capture(CrashKind.AppDomainUnhandled, captured, "AppDomain");
                };
                AppDomain.CurrentDomain.UnhandledException += _appDomainHandler;

                _taskHandler = (_, e) =>
                {
                    e.SetObserved();
                    Capture(CrashKind.UnobservedTask, e.Exception, "Task");
                };
                TaskScheduler.UnobservedTaskException += _taskHandler;

                if (diagnostics)
                {
                    AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
                        Capture(CrashKind.FirstChance, e.Exception, "FirstChance", notify: false);
                }

                EvidenceRing.Attach();
                _configured = true;
            }
        }

        private static void Capture(CrashKind kind, Exception ex, string source, bool notify = true)
        {
            try
            {
                var snap = EvidenceRing.Snapshot();
                var record = new CrashRecord
                {
                    Kind = kind,
                    Phase = snap.LastPhase,
                    Source = source,
                    ExceptionType = ex.GetType().FullName,
                    Message = ex.Message,
                    StackTrace = ex.ToString(),
                    LastLogs = snap.Logs,
                    RecentPhases = snap.Phases
                };

                CrashStore.Append(record);

                if (!notify) return;

                CrashHalt.Signal(record);
                OnCrash?.Invoke(record);
            }
            catch (Exception inner)
            {
                CrashDiag.Degrade($"ExceptionGateway 取证失败: {inner.Message}");
            }
        }

        /// <summary>测试用：解除订阅、复位。</summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                if (_threadHandler != null) Application.ThreadException -= _threadHandler;
                if (_appDomainHandler != null) AppDomain.CurrentDomain.UnhandledException -= _appDomainHandler;
                if (_taskHandler != null) TaskScheduler.UnobservedTaskException -= _taskHandler;

                _threadHandler = null;
                _appDomainHandler = null;
                _taskHandler = null;
                _configured = false;
            }
        }
    }
}
