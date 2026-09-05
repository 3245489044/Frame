
using System.Diagnostics;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LoggerIntegrationTests : LoggingIntegrationTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }

        [TestMethod]
        public void Test_Logger_MultiTargetOutput()
        {
            var result = RunTestGroup("Logger 多目标输出",
                ("Debug + Repository 同时输出", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var debugOutput = new StringWriter();
                    var originalDebug = Trace.Listeners
                        .OfType<DefaultTraceListener>().FirstOrDefault();
                    var debugListener = new TextWriterTraceListener(debugOutput);
                    Trace.Listeners.Clear();
                    Trace.Listeners.Add(debugListener);

                    try
                    {
                        var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                        {
                            EnabledCategories = LogCategory.All,
                            ActiveTargets = LogOutputTarget.Debug | LogOutputTarget.Repository
                        };

                        logger.Info(LogCategory.System, "Test multi-target message");

                        var repoCount = LogRepository.Count;
                        var debugText = debugOutput.ToString();

                        return repoCount == 1
                            && debugText.Contains("Test multi-target message");
                    }
                    finally
                    {
                        Trace.Listeners.Clear();
                        if (originalDebug != null)
                            Trace.Listeners.Add(originalDebug);
                    }
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Logger_CategoryFilter()
        {
            var result = RunTestGroup("Logger 分类开关控制",
                ("关闭分类后 Debug 无输出但 Repository 仍有", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var debugOutput = new StringWriter();
                    var originalDebug = Trace.Listeners
                        .OfType<DefaultTraceListener>().FirstOrDefault();
                    var debugListener = new TextWriterTraceListener(debugOutput);
                    Trace.Listeners.Clear();
                    Trace.Listeners.Add(debugListener);

                    try
                    {
                        var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                        {
                            EnabledCategories = LogCategories.WdUI_Render,
                            ActiveTargets = LogOutputTarget.Debug | LogOutputTarget.Repository
                        };

                        logger.Info(LogCategories.WdUI_Render, "Render message");
                        logger.Info(LogCategories.WdUI_Layout, "Layout message");

                        var repoCount = LogRepository.Count;
                        var debugText = debugOutput.ToString();

                        var repoHasRender = LogRepository.GetByCategory(LogCategories.WdUI_Render).Any();
                        var repoHasLayout = LogRepository.GetByCategory(LogCategories.WdUI_Layout).Any();

                        return repoCount == 2
                            && repoHasRender
                            && repoHasLayout
                            && debugText.Contains("Render message")
                            && !debugText.Contains("Layout message");
                    }
                    finally
                    {
                        Trace.Listeners.Clear();
                        if (originalDebug != null)
                            Trace.Listeners.Add(originalDebug);
                    }
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Logger_UiCallback()
        {
            var result = RunTestGroup("Logger UI 回调",
                ("UI 回调接收日志消息", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var receivedMessages = new List<string>();
                    Action<string> uiCallback = msg => receivedMessages.Add(msg);

                    var logger = new Common.Diagnostics.Logger(uiLogAction: uiCallback, moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.UI | LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "UI callback test");

                    return receivedMessages.Count == 1
                        && receivedMessages[0].Contains("UI callback test");
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Logger_MultipleInstances()
        {
            var result = RunTestGroup("多 Logger 实例隔离",
                ("不同模块的 Logger 写入 Repository 可分别查询", new Func<bool>(() =>
                {
                    LogRepository.Reset();

                    var wdUiLogger = new Common.Diagnostics.Logger(moduleName: "WdUI")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var fileDrivenLogger = new Common.Diagnostics.Logger(moduleName: "FileDriven")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    wdUiLogger.Info(LogCategories.WdUI_Layout, "WdUI layout message");
                    fileDrivenLogger.Info(LogCategories.FileDriven_Command, "FileDriven command message");

                    var wdUiLogs = LogRepository.GetByModule("WdUI").ToList();
                    var fileDrivenLogs = LogRepository.GetByModule("FileDriven").ToList();

                    return wdUiLogs.Count == 1
                        && fileDrivenLogs.Count == 1
                        && wdUiLogs[0].Message.Contains("WdUI layout message")
                        && fileDrivenLogs[0].Message.Contains("FileDriven command message");
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Logger_OutputTargetControl()
        {
            var result = RunTestGroup("Logger 输出目标控制",
                ("关闭 Console 目标后 Console 无输出但 Repository 仍有", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var consoleOutput = new StringWriter();
                    var originalOut = Console.Out;
                    Console.SetOut(consoleOutput);

                    try
                    {
                        var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                        {
                            EnabledCategories = LogCategory.All,
                            ActiveTargets = LogOutputTarget.Repository
                        };

                        logger.Info(LogCategory.System, "Repository only message");

                        var repoCount = LogRepository.Count;
                        var consoleText = consoleOutput.ToString();

                        return repoCount == 1
                            && !consoleText.Contains("Repository only message");
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Logger_WithCategoryTemporary()
        {
            var result = RunTestGroup("Logger 临时启用分类",
                ("WithCategory 临时启用后执行操作再恢复", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var debugOutput = new StringWriter();
                    var originalDebug = Trace.Listeners
                        .OfType<DefaultTraceListener>().FirstOrDefault();
                    var debugListener = new TextWriterTraceListener(debugOutput);
                    Trace.Listeners.Clear();
                    Trace.Listeners.Add(debugListener);

                    try
                    {
                        var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                        {
                            EnabledCategories = LogCategory.None,
                            ActiveTargets = LogOutputTarget.Debug | LogOutputTarget.Repository
                        };

                        logger.Info(LogCategory.System, "Should be filtered");

                        logger.WithCategory(LogCategory.System, () =>
                        {
                            logger.Info(LogCategory.System, "Temporarily enabled");
                        });

                        logger.Info(LogCategory.System, "Should be filtered again");

                        var repoCount = LogRepository.Count;
                        var debugText = debugOutput.ToString();

                        return repoCount == 3
                            && debugText.Contains("Temporarily enabled")
                            && !debugText.Contains("Should be filtered");
                    }
                    finally
                    {
                        Trace.Listeners.Clear();
                        if (originalDebug != null)
                            Trace.Listeners.Add(originalDebug);
                    }
                }))
            );

            AssertAllPassed(result);
        }
    }
}

