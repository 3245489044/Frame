
namespace Modules.Threading
{
    public static class ThreadingBridge
    {
        private static bool _initialized;

        private static readonly object _lock = new();

        private static AsyncOperationScheduler? _aefScheduler;

        private static WatchdogService? _aefWatchdog;

        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            ThreadContext.SetMainThread();
        }

        /// <summary>当前装配的 AEF 调度器（未装配时为 null）。</summary>
        public static AsyncOperationScheduler? AefScheduler => _aefScheduler;

        /// <summary>当前装配的 AEF 空闲看门狗（未装配时为 null）。</summary>
        public static WatchdogService? AefWatchdog => _aefWatchdog;

        /// <summary>AEF 装配：绑定调度器与其空闲看门狗。</summary>
        public static void InitializeAEF(AsyncOperationScheduler scheduler, WatchdogService watchdog)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            ArgumentNullException.ThrowIfNull(watchdog);

            lock (_lock)
            {
                _aefScheduler = scheduler;
                _aefWatchdog = watchdog;
                _aefScheduler.AttachWatchdog(watchdog);
            }
        }

        public static string GetVersion()
            => "1.0.0";

        public static string GetStatusSummary()
        {
            var activeEntries = ReentryGuard.GetActiveEntries();
            var summary = $"ThreadingBridge v{GetVersion()}\n" +
                          $"  Initialized: {_initialized}\n" +
                          $"  MainThreadId: {(ThreadContext.IsMainThread ? Environment.CurrentManagedThreadId.ToString() : "N/A")}\n" +
                          $"  Active Reentries: {activeEntries.Count}\n" +
                          $"  IsMainThread: {ThreadContext.IsMainThread}";

            var scheduler = _aefScheduler;
            if (scheduler != null)
            {
                summary += "\n  AEF ActiveOps: " + scheduler.ActiveOpCount +
                           "\n  AEF Running: " + scheduler.RunningCount +
                           "\n  AEF Queued: " + scheduler.QueuedCount +
                           "\n  AEF IdleHung: " + scheduler.IdleHungCount +
                           "\n  AEF Concurrency: " + scheduler.RunningCount + "/" + scheduler.MaxConcurrency;
            }

            return summary;
        }
    }
}
