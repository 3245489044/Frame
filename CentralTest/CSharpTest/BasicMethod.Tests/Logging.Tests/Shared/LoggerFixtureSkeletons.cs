
using System.Diagnostics;
using Common.Diagnostics;
using SkeletonFlow;

namespace Logging.Tests.Shared
{
    public static class LoggerFixtureSkeletons
    {
        #region Logger 创建

        public static ISkeleton<Unit, LoggerFixtureContext> CreateLogger(string moduleName = "TestModule")
        {
            return new DelegateSkeleton<Unit, LoggerFixtureContext>(
                $"LoggerFixture:Create({moduleName})",
                syncWithCtx: (_, _) =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: moduleName)
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    return new LoggerFixtureContext
                    {
                        Logger = logger,
                        ModuleName = moduleName
                    };
                });
        }

        public static ISkeleton<Unit, LoggerFixtureContext> CreateLogger(
            string moduleName, LogCategory enabledCategories, LogOutputTarget activeTargets)
        {
            return new DelegateSkeleton<Unit, LoggerFixtureContext>(
                $"LoggerFixture:Create({moduleName},{enabledCategories})",
                syncWithCtx: (_, _) =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: moduleName)
                    {
                        EnabledCategories = enabledCategories,
                        ActiveTargets = activeTargets
                    };
                    return new LoggerFixtureContext
                    {
                        Logger = logger,
                        ModuleName = moduleName
                    };
                });
        }

        public static ISkeleton<Unit, LoggerFixtureContext> CreateLoggerWithUiCallback(
            string moduleName = "TestModule")
        {
            return new DelegateSkeleton<Unit, LoggerFixtureContext>(
                $"LoggerFixture:CreateWithUI({moduleName})",
                syncWithCtx: (_, _) =>
                {
                    var ctx = new LoggerFixtureContext();
                    var logger = new Common.Diagnostics.Logger(
                        uiLogAction: msg => ctx.ReceivedUiMessages.Add(msg),
                        moduleName: moduleName)
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository | LogOutputTarget.UI
                    };
                    ctx.Logger = logger;
                    ctx.ModuleName = moduleName;
                    return ctx;
                });
        }

        #endregion

        #region 日志写入

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> WriteLog(
            string level, LogCategory category, string message)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:Write({level})",
                syncWithCtx: (ctx, _) =>
                {
                    switch (level.ToUpperInvariant())
                    {
                        case "DEBUG": ctx.Logger.Debug(category, message); break;
                        case "INFO":  ctx.Logger.Info(category, message); break;
                        case "WARN":  ctx.Logger.Warn(category, message); break;
                        case "ERROR": ctx.Logger.Error(category, message); break;
                        case "FATAL": ctx.Logger.Fatal(category, message); break;
                    }
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> WriteLogFormat(
            string level, LogCategory category, string format, params object?[] args)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:WriteFormat({level})",
                syncWithCtx: (ctx, _) =>
                {
                    switch (level.ToUpperInvariant())
                    {
                        case "DEBUG": ctx.Logger.Debug(category, format, args); break;
                        case "INFO":  ctx.Logger.Info(category, format, args); break;
                        case "WARN":  ctx.Logger.Warn(category, format, args); break;
                        case "ERROR": ctx.Logger.Error(category, format, args); break;
                        case "FATAL": ctx.Logger.Fatal(category, format, args); break;
                    }
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> WriteConvenienceLog(
            string method, string message)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:WriteConvenience({method})",
                syncWithCtx: (ctx, _) =>
                {
                    switch (method.ToUpperInvariant())
                    {
                        case "LOGINFO":    ctx.Logger.LogInfo(message); break;
                        case "LOGWARNING": ctx.Logger.LogWarning(message); break;
                        case "LOGERROR":   ctx.Logger.LogError(message); break;
                        case "LOGDEBUG":   ctx.Logger.LogDebug(message); break;
                    }
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> WriteLogs(
            int count, string level = "INFO", LogCategory? category = null)
        {
            var cat = category ?? LogCategory.System;
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:WriteLogs({count},{level})",
                syncWithCtx: (ctx, _) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        switch (level.ToUpperInvariant())
                        {
                            case "DEBUG": ctx.Logger.Debug(cat, $"Message {i}"); break;
                            case "INFO":  ctx.Logger.Info(cat, $"Message {i}"); break;
                            case "WARN":  ctx.Logger.Warn(cat, $"Message {i}"); break;
                            case "ERROR": ctx.Logger.Error(cat, $"Message {i}"); break;
                            case "FATAL": ctx.Logger.Fatal(cat, $"Message {i}"); break;
                        }
                    }
                    return ctx;
                });
        }

        #endregion

        #region Console 捕获

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> CaptureConsole()
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                "LoggerFixture:CaptureConsole",
                syncWithCtx: (ctx, _) =>
                {
                    var consoleOutput = new StringWriter();
                    ctx.CapturedConsoleOutput = "";
                    var originalOut = Console.Out;
                    Console.SetOut(consoleOutput);
                    ctx.Result = originalOut;
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> RestoreConsole()
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                "LoggerFixture:RestoreConsole",
                syncWithCtx: (ctx, _) =>
                {
                    if (ctx.Result is TextWriter originalOut)
                    {
                        Console.SetOut(originalOut);
                        ctx.Result = null;
                    }
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> CaptureDebug()
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                "LoggerFixture:CaptureDebug",
                syncWithCtx: (ctx, _) =>
                {
                    var debugOutput = new StringWriter();
                    ctx.CapturedDebugOutput = "";
                    var originalDebug = Trace.Listeners
                        .OfType<DefaultTraceListener>().FirstOrDefault();
                    Trace.Listeners.Clear();
                    Trace.Listeners.Add(new TextWriterTraceListener(debugOutput));
                    ctx.Result = originalDebug;
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> RestoreDebug()
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                "LoggerFixture:RestoreDebug",
                syncWithCtx: (ctx, _) =>
                {
                    Trace.Listeners.Clear();
                    if (ctx.Result is DefaultTraceListener originalDebug)
                    {
                        Trace.Listeners.Add(originalDebug);
                    }
                    ctx.Result = null;
                    return ctx;
                });
        }

        #endregion

        #region 便捷断言

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> AssertLogCount(
            string level, int expected)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:AssertLogCount({level},{expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var actual = LogRepository.GetByLevel(level).Count();
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(
                        expected, actual,
                        $"级别 {level} 的日志数量应为 {expected}，实际为 {actual}");
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> AssertLogCountByCategory(
            LogCategory category, int expected)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:AssertLogCountByCat({category},{expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var actual = LogRepository.GetByCategory(category).Count();
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(
                        expected, actual,
                        $"分类 {category} 的日志数量应为 {expected}，实际为 {actual}");
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> AssertTotalLogCount(int expected)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:AssertTotalCount({expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var actual = LogRepository.Count;
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(
                        expected, actual,
                        $"总日志数量应为 {expected}，实际为 {actual}");
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> AssertConsoleContains(string text)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:AssertConsoleContains({text})",
                syncWithCtx: (ctx, _) =>
                {
                    var consoleText = ctx.CapturedConsoleOutput ?? Console.Out.ToString() ?? "";
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                        consoleText.Contains(text),
                        $"Console 输出应包含 '{text}'，实际输出: {consoleText}");
                    return ctx;
                });
        }

        public static ISkeleton<LoggerFixtureContext, LoggerFixtureContext> AssertConsoleNotContains(string text)
        {
            return new DelegateSkeleton<LoggerFixtureContext, LoggerFixtureContext>(
                $"LoggerFixture:AssertConsoleNotContains({text})",
                syncWithCtx: (ctx, _) =>
                {
                    var consoleText = ctx.CapturedConsoleOutput ?? Console.Out.ToString() ?? "";
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsFalse(
                        consoleText.Contains(text),
                        $"Console 输出不应包含 '{text}'");
                    return ctx;
                });
        }

        #endregion
    }
}
