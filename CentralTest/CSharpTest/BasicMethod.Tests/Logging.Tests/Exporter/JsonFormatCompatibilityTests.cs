
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Exporter
{
    [TestClass]
    public class JsonFormatCompatibilityTests : ExporterTestBase
    {

        #region 测试 1: 按级别过滤

        [TestMethod]
        public void Test_Sqlite_FilterByLevel()
        {
            var result = RunTestGroup("按级别过滤",
                ("写入不同级别的日志后按级别查询", new Action(() =>
                {
                    BatchManager.Instance.RegisterBatch("test_level", "LevelTest", "test");
                    LogRepository.Write(CreateLogEntry("test_level", "INFO", "Info message"));
                    LogRepository.Write(CreateLogEntry("test_level", "WARN", "Warn message"));
                    LogRepository.Write(CreateLogEntry("test_level", "ERROR", "Error message"));

                    FlushToTestStorage("test_level");

                    var infoLogs = QueryFromTestStorage(new LogQueryFilter { Level = "INFO", Limit = 100 });
                    Assert.AreEqual(1, infoLogs.Count);
                    Assert.AreEqual("Info message", infoLogs[0].Message);

                    var errorLogs = QueryFromTestStorage(new LogQueryFilter { Level = "ERROR", Limit = 100 });
                    Assert.AreEqual(1, errorLogs.Count);
                    Assert.AreEqual("Error message", errorLogs[0].Message);

                    var multiLogs = QueryFromTestStorage(new LogQueryFilter { Level = "INFO,ERROR", Limit = 100 });
                    Assert.AreEqual(2, multiLogs.Count);
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 2: 按关键词搜索

        [TestMethod]
        public void Test_Sqlite_SearchByKeyword()
        {
            var result = RunTestGroup("按关键词搜索",
                ("写入日志后按关键词搜索", new Action(() =>
                {
                    BatchManager.Instance.RegisterBatch("test_keyword", "KeywordTest", "test");
                    LogRepository.Write(CreateLogEntry("test_keyword", "INFO", "This is a test message"));
                    LogRepository.Write(CreateLogEntry("test_keyword", "INFO", "Another log entry"));
                    LogRepository.Write(CreateLogEntry("test_keyword", "ERROR", "Error occurred in module"));

                    FlushToTestStorage("test_keyword");

                    var testLogs = QueryFromTestStorage(new LogQueryFilter { Keyword = "test", Limit = 100 });
                    Assert.AreEqual(1, testLogs.Count);
                    Assert.AreEqual("This is a test message", testLogs[0].Message);

                    var errorLogs = QueryFromTestStorage(new LogQueryFilter { Keyword = "Error", Limit = 100 });
                    Assert.AreEqual(1, errorLogs.Count);
                    Assert.AreEqual("Error occurred in module", errorLogs[0].Message);
                }))
            );

            AssertAllPassed(result);
        }

        #endregion

        #region 测试 3: 按时间范围过滤

        [TestMethod]
        public void Test_Sqlite_FilterByTimeRange()
        {
            var result = RunTestGroup("按时间范围过滤",
                ("写入日志后按时间范围查询", new Action(() =>
                {
                    BatchManager.Instance.RegisterBatch("test_time", "TimeTest", "test");
                    LogRepository.Write(CreateLogEntry("test_time", "INFO", "Old message"));
                    Thread.Sleep(100);
                    LogRepository.Write(CreateLogEntry("test_time", "INFO", "New message"));

                    FlushToTestStorage("test_time");

                    var allLogs = QueryFromTestStorage(new LogQueryFilter { Limit = 100 });
                    Assert.AreEqual(2, allLogs.Count);

                    var recentLogs = QueryFromTestStorage(new LogQueryFilter
                    {
                        FromTime = DateTime.Now.AddSeconds(-1),
                        Limit = 100
                    });
                    Assert.IsTrue(recentLogs.Count >= 1);
                }))
            );

            AssertAllPassed(result);
        }

        #endregion
    }
}
