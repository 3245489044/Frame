using BasicMethod.Pipeline;
using CentralConsole.Testing;
using CentralConsole.Testing.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineTests()
        {
            var result = RunTestGroup("Pipeline", builder =>
            {
                builder.Add("RegisterStage_ShouldAddStage", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    var stage = new MockPipelineStage("stage1");
                    pipeline.RegisterStage(stage);

                    Assert.AreEqual(1, pipeline.Stages.Count);
                    Assert.AreSame(stage, pipeline.Stages[0]);
                });

                builder.Add("RegisterStages_ShouldAddMultipleStages", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    var stages = new IPipelineStage[]
                    {
                        new MockPipelineStage("stage1"),
                        new MockPipelineStage("stage2")
                    };
                    pipeline.RegisterStages(stages);

                    Assert.AreEqual(2, pipeline.Stages.Count);
                });

                builder.Add("RegisterStage_ShouldSortByOrder", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    pipeline.RegisterStage(new MockPipelineStage("stage2") { Order = 2 });
                    pipeline.RegisterStage(new MockPipelineStage("stage1") { Order = 1 });
                    pipeline.RegisterStage(new MockPipelineStage("stage3") { Order = 3 });

                    Assert.AreEqual("stage1", pipeline.Stages[0].Name);
                    Assert.AreEqual("stage2", pipeline.Stages[1].Name);
                    Assert.AreEqual("stage3", pipeline.Stages[2].Name);
                });

                builder.Add("Execute_WithSingleStage_ShouldProcessInput", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    pipeline.RegisterStage(new MockPipelineStage("upper", (input, errors, ctx) =>
                        input?.ToString()?.ToUpper()));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("HELLO", result.Data);
                });

                builder.Add("Execute_WithMultipleStages_ShouldChainOutput", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    pipeline.RegisterStage(new MockPipelineStage("addExclamation", (input, errors, ctx) =>
                        input?.ToString() + "!"));
                    pipeline.RegisterStage(new MockPipelineStage("toUpper", (input, errors, ctx) =>
                        input?.ToString()?.ToUpper()));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("HELLO!", result.Data);
                });

                builder.Add("Execute_WithFatalStage_ShouldAbortOnFailure", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    var callOrder = new List<string>();

                    pipeline.RegisterStage(new MockPipelineStage("fatal", (input, errors, ctx) =>
                    {
                        callOrder.Add("fatal");
                        return null;
                    }) { IsFatal = true });

                    pipeline.RegisterStage(new MockPipelineStage("after", (input, errors, ctx) =>
                    {
                        callOrder.Add("after");
                        return input;
                    }));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsAborted);
                    Assert.AreEqual(1, callOrder.Count);
                    Assert.AreEqual("fatal", callOrder[0]);
                });

                builder.Add("Execute_WithNonFatalStage_ShouldContinueOnFailure", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    var callOrder = new List<string>();

                    pipeline.RegisterStage(new MockPipelineStage("nonFatal", (input, errors, ctx) =>
                    {
                        callOrder.Add("nonFatal");
                        return null;
                    }) { IsFatal = false });

                    pipeline.RegisterStage(new MockPipelineStage("after", (input, errors, ctx) =>
                    {
                        callOrder.Add("after");
                        return input;
                    }));

                    var result = pipeline.Execute("hello");

                    Assert.IsFalse(result.IsSuccess);
                    Assert.IsFalse(result.IsAborted);
                    Assert.AreEqual(2, callOrder.Count);
                    Assert.AreEqual("after", callOrder[1]);
                });

                builder.Add("Execute_StageThrowsException_Fatal_ShouldAbort", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    pipeline.RegisterStage(new MockPipelineStage("thrower", (input, errors, ctx) =>
                    {
                        throw new InvalidOperationException("test error");
                    }) { IsFatal = true });

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsAborted);
                    Assert.IsTrue(result.HasErrors);
                });

                builder.Add("Execute_StageThrowsException_NonFatal_ShouldContinue", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    var afterCalled = false;

                    pipeline.RegisterStage(new MockPipelineStage("thrower", (input, errors, ctx) =>
                    {
                        throw new InvalidOperationException("test error");
                    }) { IsFatal = false });

                    pipeline.RegisterStage(new MockPipelineStage("after", (input, errors, ctx) =>
                    {
                        afterCalled = true;
                        return input;
                    }));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(afterCalled);
                    Assert.IsFalse(result.IsSuccess);
                });

                builder.Add("Execute_WithSourceFile_ShouldPassToContext", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    string? capturedSource = null;

                    pipeline.RegisterStage(new MockPipelineStage("capture", (input, errors, ctx) =>
                    {
                        capturedSource = ctx.SourceFile;
                        return input;
                    }));

                    pipeline.Execute("hello", "test.txt");

                    Assert.AreEqual("test.txt", capturedSource);
                });

                builder.Add("Clear_ShouldRemoveAllStages", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    pipeline.RegisterStage(new MockPipelineStage("stage1"));
                    pipeline.RegisterStage(new MockPipelineStage("stage2"));

                    Assert.AreEqual(2, pipeline.Stages.Count);
                    pipeline.Clear();
                    Assert.AreEqual(0, pipeline.Stages.Count);
                });

                builder.Add("Execute_EmptyPipeline_ShouldReturnSuccess", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("hello", result.Data);
                });

                builder.Add("Execute_WithWarnings_ShouldReturnPartialSuccess", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline();
                    pipeline.RegisterStage(new MockPipelineStage("warnStage", (input, errors, ctx) =>
                    {
                        errors.Add("WARN1", "A warning", ErrorLevel.Warning);
                        return input;
                    }));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual(PipelineResultStatus.PartialSuccess, result.Status);
                });
            });
            result.AssertAllPassed();
        }
    }
}
