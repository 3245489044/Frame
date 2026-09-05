
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LoggingExtensionsTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }


        [TestMethod]
        public void Test_Extensions_AsMsLogger_Logger()
        {
            var result = RunTestGroup("LoggingExtensions AsMsLogger Logger",
                ("AsMsLogger 返回非 null", new Func<bool>(() =>
                {
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule");
                    var msLogger = logger.AsMsLogger();
                    return msLogger != null;
                })),
                ("AsMsLogger 写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.AsMsLogger();

                    msLogger.LogInformation("Test message");

                    var logs = LogRepository.GetByLevel("INFO").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Test message";
                })),
                ("AsMsLogger 各级别映射正确", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
                    {
                        EnabledCategories = LogCategory.All,
                        ActiveTargets = LogOutputTarget.Repository
                    };
                    var msLogger = logger.AsMsLogger();

                    msLogger.LogTrace("Trace msg");
                    msLogger.LogDebug("Debug msg");
                    msLogger.LogWarning("Warn msg");
                    msLogger.LogError("Error msg");
                    msLogger.LogCritical("Critical msg");

                    var debugCount = LogRepository.GetByLevel("DEBUG").Count();
                    var warnCount = LogRepository.GetByLevel("WARN").Count();
                    var errorCount = LogRepository.GetByLevel("ERROR").Count();
                    var fatalCount = LogRepository.GetByLevel("FATAL").Count();

                    return debugCount == 2
                        && warnCount == 1
                        && errorCount == 1
                        && fatalCount == 1;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Extensions_AsMsLogger_FallbackAdapter()
        {
            var result = RunTestGroup("LoggingExtensions AsMsLogger 回退适配器",
                ("非 Logger 的 ILogger 使用回退适配器", new Func<bool>(() =>
                {
                    var customLogger = new CustomTestLogger();
                    var msLogger = customLogger.AsMsLogger();

                    return msLogger != null;
                })),
                ("回退适配器 IsEnabled 返回 true", new Func<bool>(() =>
                {
                    var customLogger = new CustomTestLogger();
                    var msLogger = customLogger.AsMsLogger();

                    return msLogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
                })),
                ("回退适配器 BeginScope 返回 null", new Func<bool>(() =>
                {
                    var customLogger = new CustomTestLogger();
                    var msLogger = customLogger.AsMsLogger();

                    using var scope = msLogger.BeginScope("test");
                    return scope == null;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Extensions_CreateLogger()
        {
            var result = RunTestGroup("LoggingExtensions CreateLogger 字符串扩展",
                ("CreateLogger 返回非 null", new Func<bool>(() =>
                {
                    var logger = "TestModule".CreateLogger();
                    return logger != null;
                })),
                ("CreateLogger 模块名正确", new Func<bool>(() =>
                {
                    var logger = "MyModule".CreateLogger();
                    return logger.ModuleName == "MyModule";
                })),
                ("CreateLogger 创建的 Logger 可写入 Repository", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var logger = "TestModule".CreateLogger();
                    if (logger is Common.Diagnostics.Logger concreteLogger)
                    {
                        concreteLogger.EnabledCategories = LogCategory.All;
                        concreteLogger.ActiveTargets = LogOutputTarget.Repository;
                    }

                    logger.Info(LogCategory.System, "Extension test");

                    var logs = LogRepository.GetByModule("TestModule").ToList();
                    return logs.Count == 1
                        && logs[0].Message == "Extension test";
                })),
                ("CreateLogger 带 UI 回调", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    var receivedMessages = new List<string>();
                    Action<string> uiCallback = msg => receivedMessages.Add(msg);

                    var logger = "UiModule".CreateLogger(uiCallback);
                    if (logger is Common.Diagnostics.Logger concreteLogger)
                    {
                        concreteLogger.EnabledCategories = LogCategory.All;
                        concreteLogger.ActiveTargets = LogOutputTarget.UI | LogOutputTarget.Repository;
                    }

                    logger.Info(LogCategory.System, "UI callback test");

                    return receivedMessages.Count == 1
                        && receivedMessages[0].Contains("UI callback test");
                }))
            );

            AssertAllPassed(result);
        }
    }

    internal class CustomTestLogger : Common.Diagnostics.ILogger
    {
        public string ModuleName => "CustomTest";

        public void Debug(string category, string message) { }
        public void Debug(string category, string format, params object?[] args) { }
        public void Error(string category, string message, Exception? exception = null) { }
        public void Error(string category, string format, params object?[] args) { }
        public void Fatal(string category, string message, Exception? exception = null) { }
        public void Fatal(string category, string format, params object?[] args) { }
        public void Info(string category, string message) { }
        public void Info(string category, string format, params object?[] args) { }
        public void Warn(string category, string message) { }
        public void Warn(string category, string format, params object?[] args) { }

        public void Debug(LogCategory category, string message) { }
        public void Debug(LogCategory category, string format, params object?[] args) { }
        public void Error(LogCategory category, string message) { }
        public void Error(LogCategory category, string message, Exception exception) { }
        public void Error(LogCategory category, string format, params object?[] args) { }
        public void Fatal(LogCategory category, string message) { }
        public void Fatal(LogCategory category, string message, Exception exception) { }
        public void Fatal(LogCategory category, string format, params object?[] args) { }
        public void Info(LogCategory category, string message) { }
        public void Info(LogCategory category, string format, params object?[] args) { }

        public void Warn(LogCategory category, string message) { }
        public void Warn(LogCategory category, string format, params object?[] args) { }

        public void LogDebug(string message) { }
        public void LogDebug(string format, params object[] args) { }
        public void LogError(string message) { }
        public void LogError(string format, params object[] args) { }
        public void LogInfo(string message) { }
        public void LogInfo(string format, params object[] args) { }
        public void LogWarning(string message) { }
        public void LogWarning(string format, params object[] args) { }
    }
}

