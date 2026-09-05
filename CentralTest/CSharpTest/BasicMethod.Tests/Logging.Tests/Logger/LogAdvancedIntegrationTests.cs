
using System.Diagnostics;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LogAdvancedIntegrationTests : LoggingIntegrationTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }


        [TestMethod]
        public void Test_Logger_FatalLevel()
        {
            var result = RunTestGroup("Logger Fatal 级别",
                ("Fatal 写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Fatal(LogCategory.System, "Fatal error occurred");

                    var logs = LogRepository.GetByLevel("FATAL").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Fatal error occurred";
                })),
                ("Fatal 带异常信息", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    var ex = new InvalidOperationException("Critical failure");
                    logger.Fatal(LogCategory.System, "Fatal with exception", ex);

                    var logs = LogRepository.GetByLevel("FATAL").ToList();
                    return logs.Count == 1
                        && logs[0].Exception != null
                        && logs[0].Exception.Message == "Critical failure";
                })),
                ("Fatal 格式化方法", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Fatal(LogCategory.System, "Fatal {0}: {1}", "ERR-001", "Out of memory");

                    var logs = LogRepository.GetByLevel("FATAL").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Fatal ERR-001: Out of memory";
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_Logger_DebugLevel()
        {
            var result = RunTestGroup("Logger Debug 级别",
                ("Debug 写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Debug(LogCategory.System, "Debug message");

                    var logs = LogRepository.GetByLevel("DEBUG").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Debug message";
                })),
                ("Debug 格式化方法", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Debug(LogCategory.System, "Debug value: {0}, count: {1}", 42, 100);

                    var logs = LogRepository.GetByLevel("DEBUG").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Debug value: 42, count: 100";
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Logger_FormattedMethods()
        {
            var result = RunTestGroup("Logger 格式化方法",
                ("Info 格式化", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "User {0} logged in at {1}", "admin", DateTime.Now);

                    var logs = LogRepository.GetByLevel("INFO").ToList();
                    return logs.Count == 1
                        && logs[0].Message.Contains("admin logged in");
                })),
                ("Warn 格式化", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Warn(LogCategory.System, "Warning: {0} retries exceeded", 3);

                    var logs = LogRepository.GetByLevel("WARN").ToList();
                    return logs.Count == 1
                        && logs[0].Message.Contains("3 retries");
                })),
                ("Error 格式化", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Error(LogCategory.System, "Error in {0}: {1}", "ModuleA", "Timeout");

                    var logs = LogRepository.GetByLevel("ERROR").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Error in ModuleA: Timeout";
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Logger_ConvenienceMethods()
        {
            var result = RunTestGroup("Logger 便捷方法",
                ("LogInfo 写入 System 分类", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.LogInfo("Convenience info message");

                    var logs = LogRepository.GetByCategory(LogCategory.System).ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Convenience info message";
                })),
                ("LogWarning 写入 System 分类", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.LogWarning("Convenience warning message");

                    var logs = LogRepository.GetByCategory(LogCategory.System).ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Convenience warning message";
                })),
                ("LogError 写入 System 分类", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.LogError("Convenience error message");

                    var logs = LogRepository.GetByCategory(LogCategory.System).ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Convenience error message";
                })),
                ("LogDebug 写入 System 分类", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.LogDebug("Convenience debug message");

                    var logs = LogRepository.GetByCategory(LogCategory.System).ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Convenience debug message";
                })),
                ("LogInfo 格式化便捷方法", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.LogInfo("Format {0} + {1} = {2}", 1, 2, 3);

                    var logs = LogRepository.GetByCategory(LogCategory.System).ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Format 1 + 2 = 3";
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Logger_ModuleNameMapping()
        {
            var result = RunTestGroup("Logger 模块名称映射",
                ("FileDriven 分类映射到 FileDriven 模块", new Func<bool>(() =>
                {
                    var module = Common.Diagnostics.Logger.GetModuleName(LogCategories.FileDriven_General);
                    return module == "FileDriven";
                })),
                ("WdUI 分类映射到 WdUI 模块", new Func<bool>(() =>
                {
                    var module = Common.Diagnostics.Logger.GetModuleName(LogCategories.WdUI_Render);
                    return module == "WdUI";
                })),
                ("Launcher 分类映射到 Launcher 模块", new Func<bool>(() =>
                {
                    var module = Common.Diagnostics.Logger.GetModuleName(LogCategories.Launcher_Init);
                    return module == "Launcher";
                })),
                ("FunctionStorage 分类映射到 FunctionStorage 模块", new Func<bool>(() =>
                {
                    var module = Common.Diagnostics.Logger.GetModuleName(LogCategories.FunctionStorage_Workflow);
                    return module == "FunctionStorage";
                })),
                ("System 分类映射到 System 模块", new Func<bool>(() =>
                {
                    var module = Common.Diagnostics.Logger.GetModuleName(LogCategory.System);
                    return module == "System";
                })),
                ("未知分类映射到 General 模块", new Func<bool>(() =>
                {
                    var module = Common.Diagnostics.Logger.GetModuleName(LogCategory.None);
                    return module == "General";
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Logger_CategoryJudgment()
        {
            var result = RunTestGroup("Logger 分类归属判断",
                ("IsFileDrivenCategory 正确判断", new Func<bool>(() =>
                {
                    return Common.Diagnostics.Logger.IsFileDrivenCategory(LogCategories.FileDriven_General)
                        && Common.Diagnostics.Logger.IsFileDrivenCategory(LogCategories.FileDriven_Command)
                        && Common.Diagnostics.Logger.IsFileDrivenCategory(LogCategories.FileDriven_Inspector)
                        && !Common.Diagnostics.Logger.IsFileDrivenCategory(LogCategories.WdUI_Render);
                })),
                ("IsWdUiCategory 正确判断", new Func<bool>(() =>
                {
                    return Common.Diagnostics.Logger.IsWdUiCategory(LogCategories.WdUI_Render)
                        && Common.Diagnostics.Logger.IsWdUiCategory(LogCategories.WdUI_Layout)
                        && !Common.Diagnostics.Logger.IsWdUiCategory(LogCategories.FileDriven_Command);
                })),
                ("IsLauncherCategory 正确判断", new Func<bool>(() =>
                {
                    return Common.Diagnostics.Logger.IsLauncherCategory(LogCategories.Launcher_Init)
                        && Common.Diagnostics.Logger.IsLauncherCategory(LogCategories.Launcher_Lifecycle)
                        && !Common.Diagnostics.Logger.IsLauncherCategory(LogCategory.System);
                })),
                ("IsSystemCategory 正确判断", new Func<bool>(() =>
                {
                    return Common.Diagnostics.Logger.IsSystemCategory(LogCategory.System)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Configuration)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Network)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Database)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Security)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Performance)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Memory)
                        && Common.Diagnostics.Logger.IsSystemCategory(LogCategory.Threading)
                        && !Common.Diagnostics.Logger.IsSystemCategory(LogCategories.WdUI_Render);
                })),
                ("IsFunctionStorageCategory 正确判断", new Func<bool>(() =>
                {
                    return Common.Diagnostics.Logger.IsFunctionStorageCategory(LogCategories.FunctionStorage_General)
                        && Common.Diagnostics.Logger.IsFunctionStorageCategory(LogCategories.FunctionStorage_Workflow)
                        && Common.Diagnostics.Logger.IsFunctionStorageCategory(LogCategories.FunctionStorage_IO)
                        && Common.Diagnostics.Logger.IsFunctionStorageCategory(LogCategories.FunctionStorage_UI)
                        && !Common.Diagnostics.Logger.IsFunctionStorageCategory(LogCategory.System);
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Logger_WithCategoryTReturn()
        {
            var result = RunTestGroup("Logger WithCategory<T> 带返回值",
                ("WithCategory 返回正确值", new Func<bool>(() =>
                {
                    LogController.Configure(LogCategory.None);

                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    var result = logger.WithCategory(LogCategory.System, () =>
                    {
                        logger.Info(LogCategory.System, "Inside WithCategory");
                        return 42;
                    });

                    return result == 42;
                })),
                ("WithCategory 恢复原状态", new Func<bool>(() =>
                {
                    LogController.Configure(LogCategory.None);

                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.WithCategory(LogCategory.System, () =>
                    {
                        logger.Info(LogCategory.System, "Inside");
                    });

                    var consoleOutput = new StringWriter();
                    var originalOut = Console.Out;
                    Console.SetOut(consoleOutput);

                    try
                    {
                        logger.Info(LogCategory.System, "After restore");
                        return !consoleOutput.ToString().Contains("After restore");
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
        public void Test_Logger_MsLoggerAdapter()
        {
            var result = RunTestGroup("Logger MS ILogger 适配器",
                ("CreateMsLogger 返回非 null", new Func<bool>(() =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule");
                    var msLogger = logger.CreateMsLogger();

                    return msLogger != null;
                })),
                ("MS ILogger LogInformation 写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.CreateMsLogger();

                    msLogger.LogInformation("MS ILogger test message");

                    var logs = LogRepository.GetByLevel("INFO").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "MS ILogger test message";
                })),
                ("MS ILogger LogError 写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.CreateMsLogger();

                    msLogger.LogError(new InvalidOperationException("Test error"), "MS ILogger error");

                    var logs = LogRepository.GetByLevel("ERROR").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "MS ILogger error";
                })),
                ("MS ILogger LogWarning 写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.CreateMsLogger();

                    msLogger.LogWarning("MS ILogger warning");

                    var logs = LogRepository.GetByLevel("WARN").ToList();
                    return logs.Count == 1;
                })),
                ("MS ILogger LogCritical 写入 FATAL", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.CreateMsLogger();

                    msLogger.LogCritical("MS ILogger critical");

                    var logs = LogRepository.GetByLevel("FATAL").ToList();
                    return logs.Count == 1;
                })),
                ("MS ILogger LogDebug 写入 DEBUG", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.CreateMsLogger();

                    msLogger.LogDebug("MS ILogger debug");

                    var logs = LogRepository.GetByLevel("DEBUG").ToList();
                    return logs.Count == 1;
                })),
                ("MS ILogger IsEnabled 返回 true", new Func<bool>(() =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule");
                    var msLogger = logger.CreateMsLogger();

                    return msLogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_LogEntry_ToString()
        {
            var result = RunTestGroup("LogEntry ToString",
                ("ToString 包含时间/级别/模块/消息", new Func<bool>(() =>
                {
                    var entry = new LogEntry
                    {
                        Timestamp = new DateTime(2025, 1, 15, 10, 30, 0),
                        Level = "INFO",
                        Module = "TestModule",
                        Message = "Test message",
                        Category = LogCategory.System
                    };

                    var str = entry.ToString();
                    return str.Contains("INFO")
                        && str.Contains("TestModule")
                        && str.Contains("Test message");
                })),
                ("ToString 包含来源信息", new Func<bool>(() =>
                {
                    var entry = new LogEntry
                    {
                        Timestamp = new DateTime(2025, 1, 15, 10, 30, 0),
                        Level = "ERROR",
                        Module = "TestModule",
                        Message = "Error occurred",
                        SourceFile = "TestFile.cs",
                        SourceLine = 42
                    };

                    var str = entry.ToString();
                    return str.Contains("TestFile.cs:42");
                })),
                ("ToString 无来源时不显示", new Func<bool>(() =>
                {
                    var entry = new LogEntry
                    {
                        Timestamp = new DateTime(2025, 1, 15, 10, 30, 0),
                        Level = "INFO",
                        Module = "TestModule",
                        Message = "No source"
                    };

                    var str = entry.ToString();
                    return !str.Contains("[") || str.Count(c => c == '[') == 3;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_LogEntry_ToFullString()
        {
            var result = RunTestGroup("LogEntry ToFullString",
                ("ToFullString 包含分类信息", new Func<bool>(() =>
                {
                    var entry = new LogEntry
                    {
                        Timestamp = new DateTime(2025, 1, 15, 10, 30, 0, 500),
                        Level = "WARN",
                        Module = "TestModule",
                        Message = "Warning message",
                        Category = LogCategories.WdUI_Layout
                    };

                    var str = entry.ToFullString();
                    return str.Contains("WARN")
                        && str.Contains("TestModule")
                        && str.Contains("Category:WdUI_Layout")
                        && str.Contains("Warning message");
                })),
                ("ToFullString 包含来源信息", new Func<bool>(() =>
                {
                    var entry = new LogEntry
                    {
                        Timestamp = new DateTime(2025, 1, 15, 10, 30, 0, 500),
                        Level = "ERROR",
                        Module = "TestModule",
                        Message = "Error",
                        SourceFile = "Program.cs",
                        SourceLine = 100,
                        SourceMember = "Main"
                    };

                    var str = entry.ToFullString();
                    return str.Contains("Program.cs:100")
                        && str.Contains("Main");
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_LogEntry_SourceInfo()
        {
            var result = RunTestGroup("LogEntry 来源信息",
                ("Logger 自动填充来源信息", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Check source info");

                    var logs = LogRepository.GetAll().ToList();
                    return logs.Count == 1
                        && !string.IsNullOrEmpty(logs[0].SourceFile)
                        && logs[0].SourceLine > 0
                        && !string.IsNullOrEmpty(logs[0].SourceMember);
                })),
                ("不同日志方法填充不同来源行号", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "First message");
                    logger.Warn(LogCategory.System, "Second message");

                    var logs = LogRepository.GetAll().ToList();
                    return logs.Count == 2
                        && logs[0].SourceLine != logs[1].SourceLine;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_QueryFS_TimeFilter()
        {
            var result = RunTestGroup("LogQueryFS 时间过滤",
                ("Since 相对时间过滤（大范围匹配）", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Test message");

                    var filter = new LogQueryFilter
                    {
                        Since = "100y",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 1;
                })),
                ("Since 为空时不过滤", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Test message");

                    var filter = new LogQueryFilter
                    {
                        Since = null,
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 1;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_QueryFS_NonRecursive()
        {
            var result = RunTestGroup("LogQueryFS 非递归查询",
                ("非递归只查当前节点", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Layout message");
                    logger.Info(LogCategories.WdUI_Render, "Render message");

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI",
                        Recursive = false,
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 0;
                })),
                ("递归查询包含子节点", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Layout message");
                    logger.Info(LogCategories.WdUI_Render, "Render message");

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI",
                        Recursive = true,
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 2;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_QueryFS_EmptyResult()
        {
            var result = RunTestGroup("LogQueryFS 空结果",
                ("查询不存在的分类返回空", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Layout message");

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI/Render",
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 0
                        && queryResult.TotalCount == 0;
                })),
                ("空仓库查询返回空", new Func<bool>(() =>
                {
                    LogRepository.Reset();

                    var filter = new LogQueryFilter
                    {
                        Path = "WdUI",
                        Recursive = true,
                        Limit = 100
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 0
                        && queryResult.TotalCount == 0;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_QueryFS_LimitAndHasMore()
        {
            var result = RunTestGroup("LogQueryFS Limit 和 HasMore",
                ("Limit 限制返回条数", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    for (int i = 0; i < 10; i++)
                    {
                        logger.Info(LogCategories.WdUI_Layout, $"Message {i}");
                    }

                    var filter = new LogQueryFilter
                    {
                        Limit = 3
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.Entries.Count == 3;
                })),
                ("超过 Limit 时 HasMore=true", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    for (int i = 0; i < 10; i++)
                    {
                        logger.Info(LogCategories.WdUI_Layout, $"Message {i}");
                    }

                    var filter = new LogQueryFilter
                    {
                        Limit = 3
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return queryResult.HasMore;
                })),
                ("未超过 Limit 时 HasMore=false", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    for (int i = 0; i < 3; i++)
                    {
                        logger.Info(LogCategories.WdUI_Layout, $"Message {i}");
                    }

                    var filter = new LogQueryFilter
                    {
                        Limit = 10
                    };

                    var queryResult = LogQueryFS.Query(filter);
                    return !queryResult.HasMore;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Logger_QueryMethods()
        {
            var result = RunTestGroup("Logger 查询方法",
                ("GetLogCount 返回正确数量", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Msg 1");
                    logger.Warn(LogCategory.System, "Msg 2");
                    logger.Error(LogCategory.System, "Msg 3");

                    return LogRepository.Count == 3;
                })),
                ("GetLogsByCategory 返回正确日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategories.WdUI_Layout, "Layout msg");
                    logger.Info(LogCategories.WdUI_Render, "Render msg");

                    var logs = LogRepository.GetByCategory(LogCategories.WdUI_Layout).ToList();
                    return logs.Count == 1
                        && logs[0].Message.Contains("Layout msg");
                })),
                ("GetLogsByLevel 返回正确日志", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Info msg");
                    logger.Error(LogCategory.System, "Error msg");

                    var logs = LogRepository.GetByLevel("ERROR").ToList();
                    return logs.Count == 1
                        && logs[0].Message.Contains("Error msg");
                })),
                ("GetLogsByModule 返回正确日志", new Func<bool>(() =>
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

                    wdUiLogger.Info(LogCategories.WdUI_Layout, "WdUI msg");
                    fdLogger.Info(LogCategories.FileDriven_General, "FD msg");

                    var logs = LogRepository.GetByModule("WdUI").ToList();
                    return logs.Count == 1
                        && logs[0].Message.Contains("WdUI msg");
                })),
                ("GetErrorLogs 返回 ERROR 和 FATAL", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Info msg");
                    logger.Error(LogCategory.System, "Error msg");
                    logger.Fatal(LogCategory.System, "Fatal msg");

                    var errorLogs = LogRepository.GetByLevel("ERROR").ToList();
                    var fatalLogs = LogRepository.GetByLevel("FATAL").ToList();
                    return errorLogs.Count == 1 && errorLogs[0].Message.Contains("Error msg")
                        && fatalLogs.Count == 1 && fatalLogs[0].Message.Contains("Fatal msg");
                })),
                ("ClearLogs 清空仓库", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };

                    logger.Info(LogCategory.System, "Test msg");
                    LogRepository.Reset();

                    return LogRepository.Count == 0;
                }))
            );

            AssertAllPassed(result);
        }
    }
}
