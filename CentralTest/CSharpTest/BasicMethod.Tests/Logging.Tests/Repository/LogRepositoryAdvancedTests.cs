
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests.Repository
{
    [TestClass]
    public class LogRepositoryAdvancedTests : LoggingIntegrationTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }

        #region 测试 1: TrimExcess 自动清理

        [TestMethod]
        public void Test_Repository_TrimExcess()
        {
            var result = RunTestGroup("TrimExcess 自动清理",
                ("超出上限时移除最旧日志并保留最新 N 条", new Action(() =>
                {
                    var originalMax = LogRepository.MaxEntries;
                    LogRepository.MaxEntries = 100;

                    for (int i = 0; i < 200; i++)
                    {
                        LogRepository.Write(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = "INFO",
                            Category = LogCategory.System,
                            Module = "Test",
                            Message = $"Log #{i + 1}"
                        });
                    }

                    Assert.AreEqual(100, LogRepository.Count,
                        $"TrimExcess 后应保留 {LogRepository.MaxEntries} 条，实际为 {LogRepository.Count}");

                    var allLogs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(100, allLogs.Count);

                    var firstSeq = allLogs.First().SequenceNumber;
                    var lastSeq = allLogs.Last().SequenceNumber;
                    Assert.IsTrue(lastSeq - firstSeq + 1 <= 100,
                        $"序号范围应在 100 以内，实际范围: {firstSeq} ~ {lastSeq}");

                    LogRepository.MaxEntries = originalMax;
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 2: SequenceNumber 单调递增

        [TestMethod]
        public void Test_Repository_SequenceNumber()
        {
            var result = RunTestGroup("SequenceNumber 单调递增",
                ("写入 5 条日志验证序号从 1 开始递增", new Action(() =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        LogRepository.Write(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = "INFO",
                            Category = LogCategory.System,
                            Module = "Test",
                            Message = $"Seq #{i}"
                        });
                    }

                    var logs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(5, logs.Count);
                    for (int i = 0; i < logs.Count; i++)
                    {
                        Assert.AreEqual(i + 1, logs[i].SequenceNumber,
                            $"SequenceNumber 应为 {i + 1}，实际为 {logs[i].SequenceNumber}");
                    }
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 3: GetNewerThan 增量读取

        [TestMethod]
        public void Test_Repository_GetNewerThan()
        {
            var result = RunTestGroup("GetNewerThan 增量读取",
                ("写入 3 条后记录位置，再写入 2 条，只获取新增的", new Action(() =>
                {
                    for (int i = 0; i < 3; i++)
                        LogRepository.Write(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = "INFO",
                            Category = LogCategory.System,
                            Module = "Test",
                            Message = $"Log #{i}"
                        });

                    var lastSeq = LogRepository.GetMaxSequence();

                    for (int i = 3; i < 5; i++)
                        LogRepository.Write(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = "INFO",
                            Category = LogCategory.System,
                            Module = "Test",
                            Message = $"Log #{i}"
                        });

                    var newer = LogRepository.GetNewerThan(lastSeq).ToList();
                    Assert.AreEqual(2, newer.Count,
                        $"应返回 2 条新增日志，实际返回 {newer.Count}");
                    Assert.AreEqual(4, newer[0].SequenceNumber,
                        $"第一条新增日志序号应为 4，实际为 {newer[0].SequenceNumber}");
                    Assert.AreEqual(5, newer[1].SequenceNumber,
                        $"第二条新增日志序号应为 5，实际为 {newer[1].SequenceNumber}");
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 4: 分类索引查询

        [TestMethod]
        public void Test_Repository_CategoryIndex()
        {
            var result = RunTestGroup("分类索引查询",
                ("按 WdUI_All 和 FileDriven_All 组合分类查询", new Action(() =>
                {
                    LogRepository.Write(new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO",
                        Category = LogCategories.WdUI_Layout, Module = "WdUI",
                        Message = "Layout"
                    });
                    LogRepository.Write(new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO",
                        Category = LogCategories.WdUI_Render, Module = "WdUI",
                        Message = "Render"
                    });
                    LogRepository.Write(new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO",
                        Category = LogCategories.FileDriven_General, Module = "FileDriven",
                        Message = "File"
                    });

                    var wduiLogs = LogRepository.GetByCategory(LogCategories.WdUI_All).ToList();
                    Assert.AreEqual(2, wduiLogs.Count,
                        $"WdUI_All 应返回 2 条日志，实际返回 {wduiLogs.Count}");

                    var fdLogs = LogRepository.GetByCategory(LogCategories.FileDriven_All).ToList();
                    Assert.AreEqual(1, fdLogs.Count,
                        $"FileDriven_All 应返回 1 条日志，实际返回 {fdLogs.Count}");
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 5: 多目标输出

        [TestMethod]
        public void Test_Repository_MultiTargetOutput()
        {
            var result = RunTestGroup("多目标输出",
                ("Logger 同时输出到 Repository 和 Console", new Action(() =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "MultiTargetTest")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository | LogOutputTarget.Console
                    };

                    logger.Info(LogCategory.System, "Multi-target test message");

                    Assert.IsTrue(LogRepository.Count > 0,
                        "Repository 应包含写入的日志");
                    var logs = LogRepository.GetByModule("MultiTargetTest").ToList();
                    Assert.AreEqual(1, logs.Count,
                        "应按模块查询到写入的日志");
                    Assert.AreEqual("Multi-target test message", logs[0].Message);
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 6: 清空后 SequenceNumber 继续递增

        [TestMethod]
        public void Test_Repository_SequenceAfterClear()
        {
            var result = RunTestGroup("清空后 SequenceNumber 继续递增",
                ("Clear 后写入新日志，序号应继续递增而非重置", new Action(() =>
                {
                    for (int i = 0; i < 3; i++)
                        LogRepository.Write(new LogEntry
                        {
                            Timestamp = DateTime.Now, Level = "INFO",
                            Category = LogCategory.System, Module = "Test",
                            Message = $"Before clear #{i}"
                        });

                    var seqBeforeClear = LogRepository.GetMaxSequence();

                    LogRepository.Reset();

                    LogRepository.Write(new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO",
                        Category = LogCategory.System, Module = "Test",
                        Message = "After clear"
                    });

                    var logs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(1, logs.Count);
                    Assert.AreEqual(1, logs[0].SequenceNumber,
                        $"Reset 后写入的日志序号应为 1，实际为 {logs[0].SequenceNumber}");
                }))
            );

            AssertAllPassed(result);
        }

        #endregion
    }
}
