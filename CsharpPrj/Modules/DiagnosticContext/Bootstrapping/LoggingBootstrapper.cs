
using Common.Diagnostics;

namespace Modules.DiagnosticContext
{
    public static class LoggingBootstrapper
    {
        private static bool _initialized;

        public static Action<bool>? InitializeLogManager { get; set; }

        public static Action? ShutdownLogManager { get; set; }

        public static Action<string>? InitializeLogMessages { get; set; }

        public static Func<string, bool>? LoadLogFilter { get; set; }

        public static bool IsInitialized => _initialized;

        public static void Initialize(bool isGuiMode)
        {
            if (_initialized) return;

            InitializeLogManager?.Invoke(isGuiMode);
            _initialized = true;
        }

        public static void AutoInitialize(string? projectRoot = null)
        {
            try
            {
                if (projectRoot == null)
                {
                    try
                    {
                        // L7 修复：直接调用 BasicMethod（诊断模块已引用），消除反射依赖。
                        projectRoot = PathManager.ProjectPathManager.FindProjectRoot();
                    }
                    catch
                    {
                    }
                }

                if (projectRoot == null) return;

                string? logMessagesPath = null;
                string? filterConfigPath = null;

                try
                {
                    // L7 修复：FileDrivenPaths 下沉至 BasicMethod，消除对 Operation 层 FrontEndDriven 的反射依赖。
                    logMessagesPath = PathManager.FileDrivenPaths.GetLogMessagesFilePath();
                    filterConfigPath = PathManager.FileDrivenPaths.GetLogFilterConfigPath();
                }
                catch
                {
                }

                if (logMessagesPath == null || filterConfigPath == null)
                {
                    var (msgPath, filterPath) = GetFallbackPathsFromConfig();
                    logMessagesPath ??= msgPath;
                    filterConfigPath ??= filterPath;
                }

                logMessagesPath ??= System.IO.Path.Combine(
                    projectRoot, "FileDriven", "filedriven_log_messages.json");
                filterConfigPath ??= System.IO.Path.Combine(
                    projectRoot, "FileDriven", "LogFilterConfig.json");

                if (File.Exists(logMessagesPath))
                {
                    InitializeLogMessages?.Invoke(logMessagesPath);
                }

                LoadLogFilter?.Invoke(filterConfigPath);
            }
            catch
            {
            }
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            ShutdownLogManager?.Invoke();
            _initialized = false;
        }

        private static (string? logMessagesPath, string? filterConfigPath) GetFallbackPathsFromConfig()
        {
            try
            {
                var logMessagesPath = DiagnosticsConfigReader.GetString("log_messages", typeof(LoggingBootstrapper));
                var filterConfigPath = DiagnosticsConfigReader.GetString("log_filter_config", typeof(LoggingBootstrapper));
                return (logMessagesPath, filterConfigPath);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
