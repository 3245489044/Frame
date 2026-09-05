
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LoggingSetupIntegrationTests : LoggingIntegrationTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }

        [TestMethod]
        public void Test_Setup_InitializeRegistersAllModules()
        {
            var result = RunTestGroup("LoggingSetup 初始化注册所有模块",
                ("Initialize 后所有模块分类已注册", new Func<bool>(() =>
                {
                    var dirs = LogCategoryRegistry.ListDirectories("/logs").ToList();
                    return dirs.Any(d => d.StartsWith("WdUI"))
                        && dirs.Any(d => d.StartsWith("FileDriven"))
                        && dirs.Any(d => d.StartsWith("Launcher"))
                        && dirs.Any(d => d.StartsWith("FunctionStorage"));
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Setup_InitializeRegistersWdUI()
        {
            var result = RunTestGroup("LoggingSetup 初始化注册 WdUI 子分类",
                ("WdUI 子分类已注册", new Func<bool>(() =>
                {
                    var dirs = LogCategoryRegistry.ListDirectories("/logs/WdUI").ToList();
                    return dirs.Any(d => d.StartsWith("Render"))
                        && dirs.Any(d => d.StartsWith("Layout"))
                        && dirs.Any(d => d.StartsWith("Container"))
                        && dirs.Any(d => d.StartsWith("Resize"))
                        && dirs.Any(d => d.StartsWith("Strategy"))
                        && dirs.Any(d => d.StartsWith("Template"));
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Setup_InitializeRegistersFileDriven()
        {
            var result = RunTestGroup("LoggingSetup 初始化注册 FileDriven 子分类",
                ("FileDriven 子分类已注册", new Func<bool>(() =>
                {
                    var dirs = LogCategoryRegistry.ListDirectories("/logs/FileDriven").ToList();
                    return dirs.Any(d => d.StartsWith("General"))
                        && dirs.Any(d => d.StartsWith("Command"))
                        && dirs.Any(d => d.StartsWith("Inspector"));
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Setup_InitializeIdempotent()
        {
            var result = RunTestGroup("LoggingSetup 幂等性",
                ("多次调用 Initialize 不重复注册", new Func<bool>(() =>
                {
                    var countBefore = LogCategoryRegistry.Count;

                    LoggingSetup.Initialize();
                    var countAfter = LogCategoryRegistry.Count;

                    return countBefore == countAfter;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Setup_CreateLogger()
        {
            var result = RunTestGroup("LoggingSetup CreateLogger",
                ("CreateLogger 创建带模块名的 Logger", new Func<bool>(() =>
                {
                    LogRepository.Reset();

                    var logger = LoggingSetup.CreateLogger("TestModule");
                    logger.ActiveTargets = LogOutputTarget.Repository;
                    logger.EnabledCategories = LogCategory.All;

                    logger.Info(LogCategory.System, "Test message");

                    var logs = LogRepository.GetByModule("TestModule").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Test message";
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Setup_CreateLoggerWithCategories()
        {
            var result = RunTestGroup("LoggingSetup CreateLogger 带分类启用",
                ("CreateLogger 带分类启用", new Func<bool>(() =>
                {
                    LogRepository.Reset();

                    var logger = LoggingSetup.CreateLogger(
                        "TestModule2",
                        LogCategories.WdUI_Layout | LogCategories.WdUI_Render);
                    logger.ActiveTargets = LogOutputTarget.Repository;

                    logger.Info(LogCategories.WdUI_Layout, "Layout message");
                    logger.Info(LogCategories.FileDriven_Command, "Command message");

                    var logs = LogRepository.GetByModule("TestModule2").ToList();
                    return logs.Count == 2;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Setup_DefaultLogger()
        {
            var result = RunTestGroup("LoggingSetup Default Logger",
                ("Default Logger 可用", new Func<bool>(() =>
                {
                    LogRepository.Reset();

                    var defaultLogger = LoggingSetup.Default;
                    defaultLogger.ActiveTargets = LogOutputTarget.Repository;
                    defaultLogger.EnabledCategories = LogCategory.All;

                    defaultLogger.Info(LogCategory.System, "Default logger test");

                    var logs = LogRepository.GetByModule("Default").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Default logger test";
                }))
            );

            AssertAllPassed(result);
        }
    }
}
