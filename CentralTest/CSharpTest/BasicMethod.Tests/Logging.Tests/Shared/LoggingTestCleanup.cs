
using CentralConsole.Testing.Core;
using Common.Diagnostics;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Shared
{
    public static class LoggingTestCleanup
    {
        public static void CleanupBatchRouter()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchManager.Instance.AllBatchIds.ToList())
            {
                BatchManager.Instance.RemoveBatch(batchId);
            }
            BatchSelector.ResetToCurrent();
            BatchRouter.Instance.ResetStorage();
        }

        public static void CleanupSqliteDb()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dbPath = Path.Combine(baseDir, "logs", "batch_logs.db");
            CleanupHelper.TryDeleteFile(dbPath);
        }

        public static void CleanupEnvironmentVariables()
        {
            CleanupHelper.ClearEnvironmentVariables(
                "FILEDRIVEN_BATCH_ID",
                "FILEDRIVEN_BATCH_NAME",
                "FILEDRIVEN_BATCH_SOURCE",
                "FILEDRIVEN_BATCH_DESC",
                "FILEDRIVEN_PROJECT_ROOT");
        }

        public static void CleanupCategoryRegistry()
        {
            LogCategoryRegistry.Clear();
        }

        public static void CleanupBatchRouterFull()
        {
            CleanupBatchRouter();
            CleanupSqliteDb();
        }
    }
}
