
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests.Pipeline
{
    [TestClass]
    public class PipelineIntegrationTests : PipelineTestBase
    {
        #region 测试 1: 正常管线流程

        [TestMethod]
        public void Test_Pipeline_NormalFlow()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "PipelineTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(testRoot);

            try
            {
                RunGroup("正常管线流程",
                    TestFlow(
                        PipelineFixtureSkeletons.ClearAll()
                            .Then(PipelineFixtureSkeletons.SetEnvironmentVariables(
                                "test_pipeline_normal", "测试管线", "PipelineTest"))
                            .Then(PipelineFixtureSkeletons.RegisterBatch("test_pipeline_normal", "测试管线", "PipelineTest"))
                            .Then(PipelineFixtureSkeletons.WriteLogs(10, "INFO", "Test")),
                        PipelineAssertSkeleton.Check("执行管线", ctx =>
                        {
                            var result = LogPipeline.ExecuteWithExceptionHandling(
                                batchId: "test_pipeline_normal",
                                batchName: "测试管线",
                                source: "PipelineTest",
                                projectRoot: testRoot
                            );
                            ctx.PipelineResult = result;
                        }),
                        PipelineAssertSkeleton.PipelineSucceeded(),
                        PipelineAssertSkeleton.StorageHasLogs(),
                        PipelineAssertSkeleton.StoredLogCountEquals(10)
                    )
                );
            }
            finally
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, true);
            }
        }

        #endregion

        #region 测试 2: 异常降级路径

        [TestMethod]
        public void Test_Pipeline_EmergencyFallback()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "PipelineTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(testRoot);

            try
            {
                RunGroup("异常降级路径",
                    TestFlow(
                        PipelineFixtureSkeletons.ClearAll()
                            .Then(PipelineFixtureSkeletons.RegisterBatch("test_pipeline_fallback", "降级测试", "PipelineTest"))
                            .Then(PipelineFixtureSkeletons.WriteLogs(5, "ERROR", "Test")),
                        PipelineAssertSkeleton.Check("执行带异常处理的管线", ctx =>
                        {
                            var result = LogPipeline.ExecuteWithExceptionHandling(
                                batchId: "test_pipeline_fallback",
                                batchName: "降级测试",
                                source: "PipelineTest",
                                projectRoot: testRoot
                            );
                            ctx.PipelineResult = result;
                        }),
                        PipelineAssertSkeleton.PipelineSucceeded(),
                        PipelineAssertSkeleton.StorageHasLogs(),
                        PipelineAssertSkeleton.StoredLogCountEquals(5)
                    )
                );
            }
            finally
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, true);
            }
        }

        #endregion

        #region 测试 3: 空日志管线

        [TestMethod]
        public void Test_Pipeline_EmptyLogs()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "PipelineTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(testRoot);

            try
            {
                RunGroup("空日志管线",
                    TestFlow(
                        PipelineFixtureSkeletons.ClearAll()
                            .Then(PipelineFixtureSkeletons.RegisterBatch("test_pipeline_empty", "空日志测试", "PipelineTest")),
                        PipelineAssertSkeleton.Check("执行空日志管线", ctx =>
                        {
                            var result = LogPipeline.ExecuteWithExceptionHandling(
                                batchId: "test_pipeline_empty",
                                batchName: "空日志测试",
                                source: "PipelineTest",
                                projectRoot: testRoot
                            );
                            ctx.PipelineResult = result;
                        }),
                        PipelineAssertSkeleton.PipelineSucceeded(),
                        PipelineAssertSkeleton.StorageHasNoLogs()
                    )
                );
            }
            finally
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, true);
            }
        }

        #endregion

        #region 测试 4: 管线幂等性

        [TestMethod]
        public void Test_Pipeline_Idempotent()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "PipelineTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(testRoot);

            try
            {
                RunGroup("管线幂等性",
                    TestFlow(
                        PipelineFixtureSkeletons.ClearAll()
                            .Then(PipelineFixtureSkeletons.RegisterBatch("test_pipeline_idem", "幂等测试", "PipelineTest"))
                            .Then(PipelineFixtureSkeletons.WriteLogs(3, "INFO", "Test")),
                        PipelineAssertSkeleton.Check("第一次执行管线", ctx =>
                        {
                            var result1 = LogPipeline.ExecuteWithExceptionHandling(
                                batchId: "test_pipeline_idem",
                                batchName: "幂等测试",
                                source: "PipelineTest",
                                projectRoot: testRoot
                            );
                            ctx.PipelineResult = result1;
                        }),
                        PipelineAssertSkeleton.PipelineSucceeded(),
                        PipelineAssertSkeleton.StoredLogCountEquals(3),
                        PipelineAssertSkeleton.Check("第二次执行管线（同一批次）", ctx =>
                        {
                            var result2 = LogPipeline.ExecuteWithExceptionHandling(
                                batchId: "test_pipeline_idem",
                                batchName: "幂等测试",
                                source: "PipelineTest",
                                projectRoot: testRoot
                            );
                            Assert.IsNotNull(result2, "第二次执行应返回结果");
                            ctx.PipelineResult = result2;
                        })
                    )
                );
            }
            finally
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, true);
            }
        }

        #endregion
    }
}
