
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.EnvVars
{
    [TestClass]
    public class BatchEnvironmentTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", null);
            System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", null);
            System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", null);
            System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", null);
            System.Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", null);

            foreach (var batchId in BatchManager.Instance.AllBatchIds.ToList())
            {
                BatchManager.Instance.RemoveBatch(batchId);
            }
            LogRepository.Reset();
        }

        #region 测试 1: 环境变量 → BatchLoggerBridge 集成

        [TestMethod]
        public void Test_Environment_To_BatchLoggerBridge()
        {
            var result = RunTestGroup("环境变量 → BatchLoggerBridge 集成", builder =>
            {
                builder.Add("模拟 bat 设置环境变量并注册批次", () =>
                {
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", "Run_20260525_010000");
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", "交互启动测试");
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", "交互启动.bat");
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", "GUI 模式测试");

                    var batchId = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID")
                        ?? $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var batchName = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME")
                        ?? "测试运行";
                    var batchSource = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE")
                        ?? "Launcher";

                    BatchManager.Instance.RegisterBatch(batchId, batchName, batchSource);

                    Assert.AreEqual("Run_20260525_010000", BatchManager.Instance.CurrentBatchId);
                    var metadata = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNotNull(metadata);
                    Assert.AreEqual("交互启动测试", metadata!.Name);
                    Assert.AreEqual("交互启动.bat", metadata.Source);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 2: 环境变量缺失时的回退逻辑

        [TestMethod]
        public void Test_Environment_Fallback()
        {
            var result = RunTestGroup("环境变量缺失回退", builder =>
            {
                builder.Add("不设置环境变量时的回退逻辑", () =>
                {

                    var batchId = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID");
                    var batchName = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME")
                        ?? "自动生成批次";
                    var batchSource = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE")
                        ?? "Launcher";

                    Assert.IsNull(batchId);
                    Assert.AreEqual("自动生成批次", batchName);
                    Assert.AreEqual("Launcher", batchSource);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 3: FILEDRIVEN_PROJECT_ROOT 环境变量

        [TestMethod]
        public void Test_Environment_ProjectRoot()
        {
            var result = RunTestGroup("FILEDRIVEN_PROJECT_ROOT 环境变量", builder =>
            {
                builder.Add("设置和读取项目根目录环境变量", () =>
                {
                    var testRoot = @"D:\Test\Project";
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", testRoot);

                    var envRoot = System.Environment.GetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT");

                    Assert.AreEqual(testRoot, envRoot);
                });
            });

            result.AssertAllPassed();
        }

        #endregion

        #region 测试 4: 完整环境变量链路（模拟交互启动.bat 流程）

        [TestMethod]
        public void Test_Environment_FullChain()
        {
            var result = RunTestGroup("完整环境变量链路", builder =>
            {
                builder.Add("模拟交互启动.bat 完整流程", () =>
                {
                    var expectedBatchId = $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", expectedBatchId);
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", $"交互启动 {DateTime.Now:yyyy-MM-dd HH:mm}");
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", "交互启动.bat");
                    System.Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", "GUI 模式测试运行");

                    var batchId = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID")!;
                    var batchName = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME")!;
                    var batchSource = System.Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE")!;

                    BatchManager.Instance.RegisterBatch(batchId, batchName, batchSource);

                    Assert.AreEqual(expectedBatchId, BatchManager.Instance.CurrentBatchId);
                    var metadata = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNotNull(metadata);
                    Assert.AreEqual(batchName, metadata!.Name);
                    Assert.AreEqual("交互启动.bat", metadata.Source);
                    Assert.AreEqual(BatchStatus.Running, metadata.Status);

                    var logger = new Common.Diagnostics.Logger(moduleName: "Test");
                    logger.Info(LogCategory.System, "Test message from environment test");

                    Assert.IsTrue(LogRepository.Count > 0);
                });
            });

            result.AssertAllPassed();
        }

        #endregion
    }
}
