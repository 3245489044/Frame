
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using SkeletonFlow.App;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Batch
{
    [TestClass]
    public class BatchIntegrationTests : BatchTestBase
    {
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

        #region 完整工作流测试

        [TestMethod]
        public void Test_Integration_FullWorkflow()
        {
            RunGroup("完整工作流测试",
                FullFlowThen("batch_int_full", 10,
                    BatchAssertSkeleton.LogCountEquals("batch_int_full", 10),
                    BatchAssertSkeleton.QueryResultCount("batch_int_full", 10),
                    BatchAssertSkeleton.MetadataFieldEquals("batch_int_full", "LogCount", 10)),
                ClearThen(
                    BatchAssertSkeleton.Check("更新状态为Completed", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_int_status", "IntStatus", "test");
                        BatchManager.Instance.CompleteBatch("batch_int_status");
                        var meta = BatchManager.Instance.GetBatch("batch_int_status");
                        return meta != null && meta.Status == BatchStatus.Completed;
                    })),
                ClearThen(
                    BatchAssertSkeleton.Check("选择器查看历史", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_int_curr", "IntCurr", "test");
                        BatchManager.Instance.RegisterBatch("batch_int_hist", "IntHist", "test");
                        BatchSelector.SwitchTo("batch_int_curr");
                        return BatchSelector.IsViewingHistory()
                            && BatchSelector.GetCurrentBatch() == "batch_int_curr";
                    }))
            );
        }

        #endregion

        #region 多批次端到端测试

        [TestMethod]
        public void Test_Integration_MultipleBatches_EndToEnd()
        {
            RunGroup("多批次端到端测试",
                ClearThen(
                    BatchAssertSkeleton.Check("多批次完整流程", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_e2e_A", "E2E-A", "test");
                        for (int i = 0; i < 3; i++)
                        {
                            LogRepository.Write(CreateLogEntry("batch_e2e_A", "INFO", $"A-{i}"));
                        }

                        BatchManager.Instance.RegisterBatch("batch_e2e_B", "E2E-B", "test");
                        for (int i = 0; i < 5; i++)
                        {
                            LogRepository.Write(CreateLogEntry("batch_e2e_B", "ERROR", $"B-{i}"));
                        }

                        var countA = BatchRouter.Instance.GetLogCount("batch_e2e_A");
                        var countB = BatchRouter.Instance.GetLogCount("batch_e2e_B");

                        var logsA = BatchRouter.Instance.GetBatchLogs("batch_e2e_A");
                        var logsB = BatchRouter.Instance.GetBatchLogs("batch_e2e_B");
                        BatchManager.Instance.UpdateStats("batch_e2e_A", logsA);
                        BatchManager.Instance.UpdateStats("batch_e2e_B", logsB);

                        var metaA = BatchManager.Instance.GetBatch("batch_e2e_A");
                        var metaB = BatchManager.Instance.GetBatch("batch_e2e_B");

                        return countA == 3 && countB == 5
                            && metaA != null && metaA.ErrorCount == 0
                            && metaB != null && metaB.ErrorCount == 5;
                    }))
            );
        }

        #endregion

        #region 选择器 + 路由器集成测试

        [TestMethod]
        public void Test_Integration_SelectorAndRouter()
        {
            RunGroup("选择器与路由器集成测试",
                ClearThen(
                    BatchAssertSkeleton.Check("选择器切换后路由器查询正确", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_int_sel_A", "SelA", "test");
                        BatchManager.Instance.RegisterBatch("batch_int_sel_B", "SelB", "test");

                        LogRepository.Write(CreateLogEntry("batch_int_sel_A", "INFO", "A-data"));

                        LogRepository.Write(CreateLogEntry("batch_int_sel_B", "INFO", "B-data"));

                        BatchSelector.SwitchTo("batch_int_sel_A");
                        var selectedA = BatchSelector.GetCurrentBatch();

                        BatchSelector.SwitchTo("batch_int_sel_B");
                        var selectedB = BatchSelector.GetCurrentBatch();

                        var logsA = BatchRouter.Instance.GetBatchLogs("batch_int_sel_A")?.ToList();
                        var logsB = BatchRouter.Instance.GetBatchLogs("batch_int_sel_B")?.ToList();

                        return selectedA == "batch_int_sel_A"
                            && selectedB == "batch_int_sel_B"
                            && logsA != null && logsA.Count == 1 && logsA[0].Message == "A-data"
                            && logsB != null && logsB.Count == 1 && logsB[0].Message == "B-data";
                    }))
            );
        }

        #endregion

        #region 对比 + 选择器集成测试

        [TestMethod]
        public void Test_Integration_CompareAndSelector()
        {
            RunGroup("对比与选择器集成测试",
                ClearThen(
                    BatchAssertSkeleton.Check("对比后选择器列表更新", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_int_cmp_A", "CmpA", "test");
                        BatchManager.Instance.RegisterBatch("batch_int_cmp_B", "CmpB", "test");

                        for (int i = 0; i < 3; i++)
                        {
                            LogRepository.Write(CreateLogEntry("batch_int_cmp_A", "INFO", $"A-{i}"));
                        }
                        for (int i = 0; i < 5; i++)
                        {
                            LogRepository.Write(CreateLogEntry("batch_int_cmp_B", "INFO", $"B-{i}"));
                        }

                        var logsA = BatchRouter.Instance.GetBatchLogs("batch_int_cmp_A") ?? new List<LogEntry>();
                        var logsB = BatchRouter.Instance.GetBatchLogs("batch_int_cmp_B") ?? new List<LogEntry>();
                        var compareResult = Common.Diagnostics.LogBatchChange.CompareBatches(logsA, logsB);

                        var displayList = BatchSelector.GetBatchDisplayList();

                        return compareResult.Added == 5
                            && compareResult.Removed == 3
                            && displayList.Count == 2;
                    }))
            );
        }

        #endregion

        #region 移除 + 选择器集成测试

        [TestMethod]
        public void Test_Integration_RemoveAndSelector()
        {
            RunGroup("移除与选择器集成测试",
                ClearThen(
                    BatchAssertSkeleton.Check("移除批次后选择器列表更新", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_int_rm", "RemoveTest", "test");
                        var beforeRemove = BatchSelector.GetBatchDisplayList().Count;

                        BatchManager.Instance.RemoveBatch("batch_int_rm");
                        var afterRemove = BatchSelector.GetBatchDisplayList().Count;

                        return beforeRemove == 1 && afterRemove == 0;
                    }))
            );
        }

        #endregion

        #region 错误日志统计集成测试

        [TestMethod]
        public void Test_Integration_ErrorStats()
        {
            RunGroup("错误日志统计集成测试",
                ClearThen(
                    BatchAssertSkeleton.Check("错误日志统计正确", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_int_err", "ErrorStats", "test");

                        LogRepository.Write(CreateLogEntry("batch_int_err", "ERROR", "Err1"));
                        LogRepository.Write(CreateLogEntry("batch_int_err", "ERROR", "Err2"));
                        LogRepository.Write(CreateLogEntry("batch_int_err", "WARN", "Warn1"));
                        LogRepository.Write(CreateLogEntry("batch_int_err", "INFO", "Info1"));
                        LogRepository.Write(CreateLogEntry("batch_int_err", "INFO", "Info2"));

                        var errLogs = BatchRouter.Instance.GetBatchLogs("batch_int_err");
                        var errStats = new BatchStatistics(
                            errLogs.Count,
                            errLogs.Count(l => l.Level == "ERROR" || l.Level == "FATAL"),
                            errLogs.Count(l => l.Level == "WARN"));
                        BatchManager.Instance.UpdateStatistics("batch_int_err", errStats);

                        var meta = BatchManager.Instance.GetBatch("batch_int_err");
                        var displayList = BatchSelector.GetBatchDisplayList();
                        var displayItem = displayList.FirstOrDefault(i => i.BatchId == "batch_int_err");

                        return meta != null
                            && meta.LogCount == 5
                            && meta.ErrorCount == 2
                            && meta.WarnCount == 1
                            && displayItem != null
                            && displayItem.LogCount == 5
                            && displayItem.ErrorCount == 2;
                    }))
            );
        }

        #endregion
    }
}
