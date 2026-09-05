
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LogRepositoryIntegrationTests : LoggingIntegrationTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }

        [TestMethod]
        public void Test_Repository_QueryByCategory()
        {
            var result = RunTestGroup("LogRepository 按分类查询",
                ("按分类查询", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Layout test");
                    logger.Warn(LogCategories.FileDriven_Command, "Command warning");
                    logger.Error(LogCategory.System, "System error");

                    var layoutLogs = LogRepository.GetByCategory(LogCategories.WdUI_Layout).ToList();
                    var commandLogs = LogRepository.GetByCategory(LogCategories.FileDriven_Command).ToList();
                    var systemLogs = LogRepository.GetByCategory(LogCategory.System).ToList();

                    return layoutLogs.Count == 1
                        && commandLogs.Count == 1
                        && systemLogs.Count == 1
                        && layoutLogs[0].Message == "Layout test"
                        && commandLogs[0].Message == "Command warning"
                        && systemLogs[0].Message == "System error";
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_QueryByLevel()
        {
            var result = RunTestGroup("LogRepository 按级别查询",
                ("按级别查询", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Info message");
                    logger.Warn(LogCategory.System, "Warn message");
                    logger.Error(LogCategory.System, "Error message");
                    logger.Debug(LogCategory.System, "Debug message");

                    var infoLogs = LogRepository.GetByLevel("INFO").ToList();
                    var warnLogs = LogRepository.GetByLevel("WARN").ToList();
                    var errorLogs = LogRepository.GetByLevel("ERROR").ToList();
                    var debugLogs = LogRepository.GetByLevel("DEBUG").ToList();

                    return infoLogs.Count == 1
                        && warnLogs.Count == 1
                        && errorLogs.Count == 1
                        && debugLogs.Count == 1;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_QueryByModule()
        {
            var result = RunTestGroup("LogRepository 按模块查询",
                ("按模块查询", new Func<bool>(() =>
                {
                    LogRepository.Reset();
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

                    wdUiLogger.Info(LogCategories.WdUI_Render, "Render");
                    wdUiLogger.Info(LogCategories.WdUI_Layout, "Layout");
                    fdLogger.Info(LogCategories.FileDriven_General, "General");

                    var wdUiLogs = LogRepository.GetByModule("WdUI").ToList();
                    var fdLogs = LogRepository.GetByModule("FileDriven").ToList();

                    return wdUiLogs.Count == 2
                        && fdLogs.Count == 1;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_CombinedCategoryQuery_WdUI()
        {
            var result = RunTestGroup("LogRepository WdUI_All 组合查询",
                ("WdUI_All 获取所有 WdUI 子分类日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "WdUI")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Render, "Render");
                    logger.Info(LogCategories.WdUI_Layout, "Layout");
                    logger.Info(LogCategories.WdUI_Container, "Container");
                    logger.Info(LogCategories.WdUI_Resize, "Resize");
                    logger.Info(LogCategories.WdUI_Strategy, "Strategy");
                    logger.Info(LogCategories.WdUI_Template, "Template");
                    logger.Info(LogCategories.WdUI_Pipeline, "Pipeline");

                    var allWdUiLogs = LogRepository.GetByCategory(LogCategories.WdUI_All).ToList();
                    return allWdUiLogs.Count == 7;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_CombinedCategoryQuery_FileDriven()
        {
            var result = RunTestGroup("LogRepository FileDriven_All 组合查询",
                ("FileDriven_All 获取所有 FileDriven 子分类日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "FileDriven")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.FileDriven_General, "General");
                    logger.Info(LogCategories.FileDriven_Command, "Command");
                    logger.Info(LogCategories.FileDriven_Inspector, "Inspector");

                    var allFdLogs = LogRepository.GetByCategory(LogCategories.FileDriven_All).ToList();
                    return allFdLogs.Count == 3;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_CustomQuery_Keyword()
        {
            var result = RunTestGroup("LogRepository 关键词查询",
                ("按关键词过滤", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "This is a test message");
                    logger.Info(LogCategory.System, "Another info message");
                    logger.Warn(LogCategory.System, "Warning: something happened");

                    var withTest = LogRepository.Query(e => e.Message.Contains("test", StringComparison.OrdinalIgnoreCase)).ToList();
                    var withWarning = LogRepository.Query(e => e.Message.Contains("Warning", StringComparison.OrdinalIgnoreCase)).ToList();

                    return withTest.Count == 1
                        && withWarning.Count == 1;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_CombinedFilterQuery()
        {
            var result = RunTestGroup("LogRepository 组合过滤查询",
                ("组合查询（分类 + 级别 + 模块）", new Func<bool>(() =>
                {
                    LogRepository.Reset();
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

                    wdUiLogger.Info(LogCategories.WdUI_Layout, "Layout info");
                    wdUiLogger.Error(LogCategories.WdUI_Render, "Render error");
                    fdLogger.Info(LogCategories.FileDriven_General, "General info");
                    fdLogger.Error(LogCategories.FileDriven_Command, "Command error");

                    var wdUiErrors = LogRepository.Query(
                        category: LogCategories.WdUI_All,
                        level: "ERROR",
                        module: "WdUI").ToList();

                    var fdInfos = LogRepository.Query(
                        category: LogCategories.FileDriven_All,
                        level: "INFO",
                        module: "FileDriven").ToList();

                    return wdUiErrors.Count == 1
                        && wdUiErrors[0].Message == "Render error"
                        && fdInfos.Count == 1
                        && fdInfos[0].Message == "General info";
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_CapacityLimit()
        {
            var result = RunTestGroup("LogRepository 容量限制",
                ("超过上限时自动移除最旧日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var originalMax = LogRepository.MaxEntries;
                    LogRepository.MaxEntries = 20;

                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    for (int i = 0; i < 25; i++)
                    {
                        logger.Info(LogCategory.System, $"Message {i}");
                    }

                    var count = LogRepository.Count;
                    LogRepository.MaxEntries = originalMax;

                    return count <= 20 && count >= 18;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_Clear()
        {
            var result = RunTestGroup("LogRepository 清空操作",
                ("Clear 后所有索引和队列为空", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Test");
                    logger.Warn(LogCategories.WdUI_Layout, "Layout warn");
                    logger.Error(LogCategories.FileDriven_Command, "Command error");

                    LogRepository.Reset();

                    return LogRepository.Count == 0
                        && !LogRepository.GetByCategory(LogCategory.System).Any()
                        && !LogRepository.GetByLevel("INFO").Any()
                        && !LogRepository.GetByModule("TestModule").Any();
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Repository_Disabled()
        {
            var result = RunTestGroup("LogRepository 禁用收集",
                ("Enabled = false 时写入变为空操作", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    LogRepository.Enabled = false;

                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Should not be stored");

                    var count = LogRepository.Count;
                    LogRepository.Enabled = true;

                    return count == 0;
                }))
            );

            AssertAllPassed(result);
        }
    }
}
