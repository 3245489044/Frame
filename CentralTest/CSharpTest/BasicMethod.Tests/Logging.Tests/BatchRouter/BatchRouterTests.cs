
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.BatchRouterTests
{
    [TestClass]
    public class BatchRouterTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LoggingTestCleanup.CleanupBatchRouter();
        }

        private static LogEntry CreateLogEntry(string batchId, string level, string message, LogCategory category = LogCategory.System)
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

        #region 测试 1: 批次注册与元数据

        [TestMethod]
        public void Test_BatchRouter_RegisterAndMetadata()
        {
            var result = RunTestGroup("批次注册与元数据", builder =>
            {
                builder.Add("注册批次后应能查询到元数据", () =>
                {
                    var batchId = "test_batch_001";
                    var metadata = new BatchMetadata
                    {
                        Name = "测试批次",
                        Source = "Test",
                        Status = BatchStatus.Running
                    };

                    BatchManager.Instance.RegisterBatch(batchId, metadata.Name, metadata.Source);

                    var retrieved = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNotNull(retrieved);
                    Assert.AreEqual("测试批次", retrieved!.Name);
                    Assert.AreEqual("Test", retrieved.Source);
                    Assert.AreEqual(BatchStatus.Running, retrieved.Status);
                    Assert.AreEqual(batchId, retrieved.BatchId);
                });

                builder.Add("移除批次后元数据应消失", () =>
                {
                    var batchId = "test_batch_002";
                    BatchManager.Instance.RegisterBatch(batchId, "待移除", "test");

                    BatchManager.Instance.RemoveBatch(batchId);

                    Assert.IsNull(BatchManager.Instance.GetBatch(batchId));
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 2: 日志隔离查询

        [TestMethod]
        public void Test_BatchRouter_IsolatedStorage()
        {
            var result = RunTestGroup("日志隔离查询", builder =>
            {
                builder.Add("不同批次的日志应隔离查询", () =>
                {
                    BatchManager.Instance.RegisterBatch("batch_A", "BatchA", "test");
                    BatchManager.Instance.RegisterBatch("batch_B", "BatchB", "test");

                    LogRepository.Write(CreateLogEntry("batch_A", "INFO", "Batch A message"));
                    LogRepository.Write(CreateLogEntry("batch_B", "ERROR", "Batch B message"));

                    var logsA = BatchRouter.Instance.GetBatchLogs("batch_A").ToList();
                    var logsB = BatchRouter.Instance.GetBatchLogs("batch_B").ToList();

                    Assert.AreEqual(1, logsA.Count);
                    Assert.AreEqual(1, logsB.Count);
                    Assert.AreEqual("Batch A message", logsA[0].Message);
                    Assert.AreEqual("Batch B message", logsB[0].Message);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 3: 批次状态管理

        [TestMethod]
        public void Test_BatchRouter_StatusManagement()
        {
            var result = RunTestGroup("批次状态管理", builder =>
            {
                builder.Add("批次状态应能正确流转", () =>
                {
                    var batchId = "test_status";
                    BatchManager.Instance.RegisterBatch(batchId, "StatusTest", "test");

                    BatchManager.Instance.CompleteBatch(batchId);
                    var meta = BatchManager.Instance.GetBatch(batchId);
                    Assert.AreEqual(BatchStatus.Completed, meta!.Status);

                    BatchManager.Instance.FailBatch(batchId);
                    meta = BatchManager.Instance.GetBatch(batchId);
                    Assert.AreEqual(BatchStatus.Failed, meta!.Status);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 4: 多批次并发隔离

        [TestMethod]
        public void Test_BatchRouter_ConcurrentIsolation()
        {
            var result = RunTestGroup("多批次并发隔离", builder =>
            {
                builder.Add("多个批次同时存在时日志不混淆", () =>
                {
                    BatchManager.Instance.RegisterBatch("batch_1", "Batch1", "test");
                    BatchManager.Instance.RegisterBatch("batch_2", "Batch2", "test");
                    BatchManager.Instance.RegisterBatch("batch_3", "Batch3", "test");

                    for (int i = 0; i < 3; i++)
                    {
                        LogRepository.Write(CreateLogEntry("batch_1", "INFO", $"Batch 1 - log {i}"));
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        LogRepository.Write(CreateLogEntry("batch_2", "WARN", $"Batch 2 - log {i}"));
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        LogRepository.Write(CreateLogEntry("batch_3", "ERROR", $"Batch 3 - log {i}"));
                    }

                    Assert.AreEqual(3, BatchRouter.Instance.GetBatchLogs("batch_1").Count());
                    Assert.AreEqual(5, BatchRouter.Instance.GetBatchLogs("batch_2").Count());
                    Assert.AreEqual(2, BatchRouter.Instance.GetBatchLogs("batch_3").Count());

                    Assert.IsTrue(BatchRouter.Instance.GetBatchLogs("batch_1").All(l => l.Level == "INFO"));
                    Assert.IsTrue(BatchRouter.Instance.GetBatchLogs("batch_2").All(l => l.Level == "WARN"));
                    Assert.IsTrue(BatchRouter.Instance.GetBatchLogs("batch_3").All(l => l.Level == "ERROR"));
                });
            });

            result.AssertAllPassed();
        }

        #endregion
    }
}
