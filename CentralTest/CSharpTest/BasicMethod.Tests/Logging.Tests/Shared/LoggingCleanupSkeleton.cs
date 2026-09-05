
using CentralConsole.Testing.Core;
using Common.Diagnostics;
using SkeletonFlow;
using SkeletonFlow.App;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Shared
{
    public static class LoggingCleanupSkeleton
    {
        public static ISkeleton<Unit, Unit> ClearAll()
        {
            return new DelegateSkeleton<Unit, Unit>("ClearAllLoggingState", (_, _) =>
            {
                LogRepository.Reset();
                BatchManager.Instance.Reset();
                BatchSelector.ResetToCurrent();
                BatchRouter.Instance.ResetStorage();
                DeleteSqliteDb();
                return Unit.Default;
            });
        }

        public static ISkeleton<Unit, Batch.BatchFixtureContext> ClearAllToBatchContext()
        {
            return new DelegateSkeleton<Unit, Batch.BatchFixtureContext>("ClearAllToBatchContext", (_, _) =>
            {
                LogRepository.Reset();
                BatchManager.Instance.Reset();
                BatchSelector.ResetToCurrent();
                DeleteSqliteDb();

                var tempPath = System.IO.Path.GetTempFileName();
                var storage = new SqliteIsolatedStorage(tempPath);
                storage.Initialize();
                return new Batch.BatchFixtureContext
                {
                    TempDbPath = tempPath,
                    Storage = storage
                };
            });
        }

        public static ISkeleton<Unit, Pipeline.PipelineFixtureContext> ClearAllToPipelineContext()
        {
            return new DelegateSkeleton<Unit, Pipeline.PipelineFixtureContext>("ClearAllToPipelineContext", (_, _) =>
            {
                LogRepository.Reset();
                BatchManager.Instance.Reset();
                BatchSelector.ResetToCurrent();
                DeleteSqliteDb();
                return new Pipeline.PipelineFixtureContext();
            });
        }

        private static void DeleteSqliteDb()
        {
            try
            {
                var defaultDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Logs",
                    "logs.db");
                CleanupHelper.TryDeleteFile(defaultDbPath);

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var testDbPath = Path.Combine(baseDir, "logs", "batch_logs.db");
                CleanupHelper.TryDeleteFile(testDbPath);
            }
            catch { }
        }
    }
}
