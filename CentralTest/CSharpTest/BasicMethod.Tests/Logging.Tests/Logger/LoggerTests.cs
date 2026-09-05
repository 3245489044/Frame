
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.LoggerTests
{
    [TestClass]
    public class LoggerTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchManager.Instance.AllBatchIds.ToList())
            {
                BatchManager.Instance.RemoveBatch(batchId);
            }
        }

        #region 测试 1: 日志写入与级别

        [TestMethod]
        public void Test_Logger_WriteAndLevel()
        {
            var result = RunTestGroup("日志写入与级别", builder =>
            {
                builder.Add("写入不同级别的日志", () =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule");

                    logger.Debug(LogCategory.System, "Debug message");
                    logger.Info(LogCategory.System, "Info message");
                    logger.Warn(LogCategory.System, "Warn message");
                    logger.Error(LogCategory.System, "Error message");
                    logger.Fatal(LogCategory.System, "Fatal message");

                    var allLogs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(5, allLogs.Count);

                    var levels = allLogs.Select(l => l.Level).ToList();
                    Assert.IsTrue(levels.Contains("DEBUG"));
                    Assert.IsTrue(levels.Contains("INFO"));
                    Assert.IsTrue(levels.Contains("WARN"));
                    Assert.IsTrue(levels.Contains("ERROR"));
                    Assert.IsTrue(levels.Contains("FATAL"));
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 2: 日志分类

        [TestMethod]
        public void Test_Logger_Categories()
        {
            var result = RunTestGroup("日志分类", builder =>
            {
                builder.Add("写入不同分类的日志", () =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule");

                    logger.Info(LogCategory.System, "System message");
                    logger.Info(LogCategory.Performance, "Performance message");
                    logger.Info(LogCategory.Security, "Security message");
                    logger.Info(LogCategory.Database, "Database message");

                    var allLogs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(4, allLogs.Count);

                    var categories = allLogs.Select(l => l.Category).ToList();
                    Assert.IsTrue(categories.Contains(LogCategory.System));
                    Assert.IsTrue(categories.Contains(LogCategory.Performance));
                    Assert.IsTrue(categories.Contains(LogCategory.Security));
                    Assert.IsTrue(categories.Contains(LogCategory.Database));
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 3: 模块化日志

        [TestMethod]
        public void Test_Logger_ModuleIsolation()
        {
            var result = RunTestGroup("模块化日志", builder =>
            {
                builder.Add("不同模块的日志应正确标记", () =>
                {
                    var loggerA = new Common.Diagnostics.Logger(moduleName: "ModuleA");
                    var loggerB = new Common.Diagnostics.Logger(moduleName: "ModuleB");

                    loggerA.Info(LogCategory.System, "Message from A");
                    loggerB.Info(LogCategory.System, "Message from B");

                    var allLogs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(2, allLogs.Count);

                    var moduleA = allLogs.First(l => l.Module == "ModuleA");
                    var moduleB = allLogs.First(l => l.Module == "ModuleB");
                    Assert.AreEqual("Message from A", moduleA.Message);
                    Assert.AreEqual("Message from B", moduleB.Message);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 4: 日志上下文

        [TestMethod]
        public void Test_Logger_Context()
        {
            var result = RunTestGroup("日志上下文", builder =>
            {
                builder.Add("日志应包含调用上下文信息", () =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule");

                    logger.Info(LogCategory.System, "Context test message");

                    var allLogs = LogRepository.GetAll().ToList();
                    Assert.AreEqual(1, allLogs.Count);

                    var entry = allLogs[0];
                    Assert.IsNotNull(entry.Timestamp);
                    Assert.AreEqual("INFO", entry.Level);
                    Assert.AreEqual(LogCategory.System, entry.Category);
                    Assert.AreEqual("TestModule", entry.Module);
                    Assert.AreEqual("Context test message", entry.Message);
                });
            });

            result.AssertAllPassed();
        }

        #endregion
    }
}
