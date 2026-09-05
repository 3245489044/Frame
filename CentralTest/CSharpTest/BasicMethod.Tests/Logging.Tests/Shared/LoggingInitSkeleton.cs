
using Common.Diagnostics;

namespace Logging.Tests.Shared
{
    public static class LoggingInitSkeleton
    {
        private static readonly object _initLock = new();
        private static bool _classInitialized;

        public static void EnsureInitialized()
        {
            lock (_initLock)
            {
                if (!_classInitialized)
                {
                    LoggingSetup.Reset();
                    LogCategoryRegistry.Clear();
                    LoggingSetup.Initialize();
                    _classInitialized = true;
                }
            }
        }

        public static void Reset()
        {
            lock (_initLock)
            {
                _classInitialized = false;
            }
        }
    }
}
