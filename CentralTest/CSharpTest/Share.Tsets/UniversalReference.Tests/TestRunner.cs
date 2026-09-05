using System.Reflection;
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BasicMethod.Tests.UniversalReference
{
    [TestClass]
    [TestGroup("UniversalReference")]
    public class TestRunner : TestBase
    {
        private static ILoggerFactory _logFactory = null!;
        private static ILogger _testLogger = null!;

        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            _logFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            _testLogger = _logFactory.CreateLogger("UniversalReference.Tests");
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            _logFactory?.Dispose();
        }

        [TestMethod]
        public async Task RunAllExamplesAsync()
        {
            _testLogger.LogInformation("===== 开始运行通用工作流框架测试 =====");

            try
            {
                var example = new CompleteUsageExample();
                
                var logFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                var logger = logFactory.CreateLogger("UniversalReference.Tests.CompleteUsageExample");
                
                var loggerField = typeof(CompleteUsageExample)
                    .GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic);
                if (loggerField != null)
                {
                    loggerField.SetValue(example, logger);
                }
                
                await example.RunAllExamplesAsync();
            }
            catch (Exception ex)
            {
                _testLogger.LogError(ex, "运行测试时发生错误");
                throw;
            }

            _testLogger.LogInformation("===== 测试运行完成 =====");
        }
    }
}
