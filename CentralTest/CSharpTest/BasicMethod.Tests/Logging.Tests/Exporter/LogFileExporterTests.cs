
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Exporter
{
    [TestClass]
    public class LogFileExporterTests : ExporterTestBase
    {

        #region 测试 1: SQLite 写入与查询

        [TestMethod]
        public void Test_Sqlite_WriteAndQuery()
        {
            var result = RunTestGroup("SQLite 写入与查询", builder =>
            {
                builder.Add("写入后查询验证字段", () =>
                {
                    BatchManager.Instance.RegisterBatch("test_sqlite", "SqliteTest", "test");
                    LogRepository.Write(CreateLogEntry("test_sqlite", "INFO", "Test message"));

                    var count = FlushToTestStorage("test_sqlite");
                    Assert.AreEqual(1, count);

                    var logs = QueryFromTestStorage(new LogQueryFilter { Limit = 100 });
                    Assert.AreEqual(1, logs.Count);

                    var entry = logs[0];
                    Assert.AreEqual("INFO", entry.Level);
                    Assert.AreEqual("Test message", entry.Message);
                    Assert.AreEqual("Test", entry.Module);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 2: 空批次刷入

        [TestMethod]
        public void Test_Sqlite_EmptyBatch()
        {
            var result = RunTestGroup("空批次刷入", builder =>
            {
                builder.Add("无日志时刷入应返回 0", () =>
                {
                    BatchManager.Instance.RegisterBatch("test_empty", "Empty", "test");

                    var count = FlushToTestStorage("test_empty");

                    Assert.AreEqual(0, count);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 3: 多批次隔离

        [TestMethod]
        public void Test_Sqlite_MultipleBatches()
        {
            var result = RunTestGroup("多批次隔离", builder =>
            {
                builder.Add("不同批次刷入后查询隔离", () =>
                {
                    BatchManager.Instance.RegisterBatch("batch_A", "BatchA", "test");
                    LogRepository.Write(CreateLogEntry("batch_A", "INFO", "A-1"));

                    BatchManager.Instance.RegisterBatch("batch_B", "BatchB", "test");
                    LogRepository.Write(CreateLogEntry("batch_B", "ERROR", "B-1"));

                    var countA = FlushToTestStorage("batch_A");
                    var countB = FlushToTestStorage("batch_B");

                    Assert.AreEqual(1, countA);
                    Assert.AreEqual(1, countB);

                    var allLogs = QueryFromTestStorage(new LogQueryFilter { Limit = 100 });
                    Assert.AreEqual(2, allLogs.Count);

                    var infoLogs = QueryFromTestStorage(new LogQueryFilter { Level = "INFO", Limit = 100 });
                    Assert.AreEqual(1, infoLogs.Count);
                    Assert.AreEqual("A-1", infoLogs[0].Message);

                    var errorLogs = QueryFromTestStorage(new LogQueryFilter { Level = "ERROR", Limit = 100 });
                    Assert.AreEqual(1, errorLogs.Count);
                    Assert.AreEqual("B-1", errorLogs[0].Message);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 4: 计数与清空

        [TestMethod]
        public void Test_Sqlite_CountAndClear()
        {
            var result = RunTestGroup("计数与清空", builder =>
            {
                builder.Add("写入后计数，清空后计数为 0", () =>
                {
                    BatchManager.Instance.RegisterBatch("test_count", "CountTest", "test");
                    LogRepository.Write(CreateLogEntry("test_count", "INFO", "Msg 1"));
                    LogRepository.Write(CreateLogEntry("test_count", "WARN", "Msg 2"));
                    LogRepository.Write(CreateLogEntry("test_count", "ERROR", "Msg 3"));

                    FlushToTestStorage("test_count");

                    Assert.AreEqual(3, TestStorage.Count());

                    TestStorage.Clear();

                    Assert.AreEqual(0, TestStorage.Count());
                });
            });

            result.AssertAllPassed();
        }

        #endregion
    }
}
