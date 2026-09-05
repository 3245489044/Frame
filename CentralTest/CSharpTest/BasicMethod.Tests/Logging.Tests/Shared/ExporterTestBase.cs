
using CentralConsole.Testing.Core;
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Shared
{
    public abstract class ExporterTestBase : LoggingTestBase<Unit>
    {
        protected string TestOutputDir { get; private set; } = null!;

        protected IIsolatedStorage TestStorage { get; private set; } = null!;

        [TestInitialize]
        public void InitExporter()
        {
            TestOutputDir = CleanupHelper.CreateTempDirectory(GetType().Name);

            var dbPath = Path.Combine(TestOutputDir, "test_logs.db");
            TestStorage = new SqliteIsolatedStorage(dbPath);
            TestStorage.Initialize();

            BatchManager.Instance.Reset();
            LogRepository.Reset();
        }

        protected override void CleanupTest()
        {
            TestStorage?.Close();
            CleanupHelper.TryDeleteDirectory(TestOutputDir);
            LoggingTestCleanup.CleanupBatchRouter();
        }

        protected static LogEntry CreateLogEntry(string batchId, string level, string message, LogCategory category = LogCategory.System)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Module = "Test",
                Message = message
            };
            entry.Extensions["BatchId"] = batchId;
            return entry;
        }

        protected static void RegisterBatchWithLog(string batchId, string name, string message, string level = "INFO")
        {
            BatchManager.Instance.RegisterBatch(batchId, name, "test");
            LogRepository.Write(CreateLogEntry(batchId, level, message));
        }

        protected int FlushToTestStorage(string batchId)
        {
            return BatchRouter.Instance.FlushToIsolatedStorage(batchId, TestStorage);
        }

        protected List<LogEntry> QueryFromTestStorage(LogQueryFilter filter)
        {
            return TestStorage.Query(filter).ToList();
        }
    }
}
