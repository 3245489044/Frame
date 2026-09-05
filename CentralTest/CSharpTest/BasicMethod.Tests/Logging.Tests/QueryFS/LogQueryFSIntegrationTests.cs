
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LogQueryFSIntegrationTests : LoggingTestBase<Unit>
    {
        private static readonly object _initLock = new();
        private static bool _classInitialized;

        [TestInitialize]
        public void TestInitialize()
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
            LogRepository.Reset();
        }

        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }

        private static void WriteTestLogs()
        {
            var wdUiLogger = new Common.Diagnostics.Logger(moduleName: "WdUI")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };
            var fdLogger = new Common.Diagnostics.Logger(moduleName: "FileDriven")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };

            wdUiLogger.Info(LogCategories.WdUI_Layout, "Layout calculation started");
            wdUiLogger.Info(LogCategories.WdUI_Render, "Rendering frame 1");
            wdUiLogger.Warn(LogCategories.WdUI_Layout, "Layout overflow detected");
            wdUiLogger.Error(LogCategories.WdUI_Render, "Render failed: out of memory");
            fdLogger.Info(LogCategories.FileDriven_General, "File loaded successfully");
            fdLogger.Info(LogCategories.FileDriven_Command, "Executing command: build");
            fdLogger.Error(LogCategories.FileDriven_Command, "Command failed: file not found");
        }

        [TestMethod]
        public void Test_QueryFS_QueryByPath_Layout()
        {
            var result = RunTestGroup("LogQueryFS 按路径查询",
                ("查询 WdUI/Layout 路径", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI/Layout",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 2
                        && queryResult.MatchedCategories.Count == 1
                        && queryResult.MatchedCategories[0] == "WdUI/Layout";
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_QueryByPath_WdUI()
        {
            var result = RunTestGroup("LogQueryFS 按路径查询 WdUI",
                ("查询 WdUI 根路径（递归）", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI",
                        Recursive = true,
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 4;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_QueryByPath_FileDriven()
        {
            var result = RunTestGroup("LogQueryFS 按路径查询 FileDriven",
                ("查询 FileDriven 根路径（递归）", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Path = "FileDriven",
                        Recursive = true,
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 3;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_QueryByLevel_Error()
        {
            var result = RunTestGroup("LogQueryFS 按级别过滤 ERROR",
                ("只查询 ERROR 级别", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Level = "ERROR",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 2
                        && queryResult.Entries.All(e => e.Level == "ERROR");
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_QueryByLevel_ErrorWarn()
        {
            var result = RunTestGroup("LogQueryFS 按级别过滤 ERROR,WARN",
                ("查询 ERROR 和 WARN 级别", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Level = "ERROR,WARN",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 3;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_QueryByKeyword_Failed()
        {
            var result = RunTestGroup("LogQueryFS 关键词搜索 'failed'",
                ("搜索包含 'failed' 的日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Keyword = "failed",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 2;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_QueryByKeyword_Layout()
        {
            var result = RunTestGroup("LogQueryFS 关键词搜索 'Layout'",
                ("搜索包含 'Layout' 的日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Keyword = "Layout",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 2;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_GetStats_WdUI()
        {
            var result = RunTestGroup("LogQueryFS 统计 WdUI",
                ("获取 WdUI 路径的统计", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var stats = LogQueryFS.GetStats("WdUI");

                    return stats.TotalCount == 4
                        && stats.ByLevel.ContainsKey("INFO")
                        && stats.ByLevel.ContainsKey("WARN")
                        && stats.ByLevel.ContainsKey("ERROR")
                        && stats.ByLevel["INFO"] == 2
                        && stats.ByLevel["WARN"] == 1
                        && stats.ByLevel["ERROR"] == 1;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_GetStats_FileDriven()
        {
            var result = RunTestGroup("LogQueryFS 统计 FileDriven",
                ("获取 FileDriven 路径的统计", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var stats = LogQueryFS.GetStats("FileDriven");

                    return stats.TotalCount == 3
                        && stats.ByLevel["INFO"] == 2
                        && stats.ByLevel["ERROR"] == 1;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_ListDirectories()
        {
            var result = RunTestGroup("LogQueryFS 目录列表",
                ("列出 /logs 下的模块目录", new Func<bool>(() =>
                {
                    var dirs = LogQueryFS.ListDirectories("/logs").ToList();
                    return dirs.Any(d => d.StartsWith("WdUI"))
                        && dirs.Any(d => d.StartsWith("FileDriven"))
                        && dirs.Any(d => d.StartsWith("Launcher"))
                        && dirs.Any(d => d.StartsWith("FunctionStorage"));
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_CombinedFilter()
        {
            var result = RunTestGroup("LogQueryFS 组合过滤",
                ("路径 + 级别 + 关键词组合查询", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI",
                        Level = "ERROR",
                        Keyword = "Render",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 1
                        && queryResult.Entries[0].Message.Contains("Render failed");
                }))
            );

            AssertAllPassed(result);
        }
    }
}

