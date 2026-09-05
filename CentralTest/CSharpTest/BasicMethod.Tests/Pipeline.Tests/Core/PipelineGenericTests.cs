using BasicMethod.Pipeline;
using CentralConsole.Testing;
using CentralConsole.Testing.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineGenericTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineGenericTests()
        {
            var result = RunTestGroup("Pipeline<TInput, TOutput>", builder =>
            {
                builder.Add("RegisterStage_ShouldAddTypedStage", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    var stage = new MockPipelineStage<string, string>("stage1");
                    pipeline.RegisterStage(stage);

                    Assert.AreEqual(1, pipeline.Stages.Count);
                    Assert.AreSame(stage, pipeline.Stages[0]);
                });

                builder.Add("Execute_WithSingleStage_ShouldProcessInput", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("upper",
                        (input, errors, ctx) => input.ToUpper()));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("HELLO", result.Data);
                });

                builder.Add("Execute_WithMultipleStages_ShouldChainOutput", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("addExclamation",
                        (input, errors, ctx) => input + "!"));
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("toUpper",
                        (input, errors, ctx) => input.ToUpper()));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("HELLO!", result.Data);
                });

                builder.Add("Execute_WithFatalStage_ShouldAbortOnFailure", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    var callOrder = new List<string>();

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("fatal",
                        (input, errors, ctx) =>
                        {
                            callOrder.Add("fatal");
                            return null;
                        }) { IsFatal = true });

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("after",
                        (input, errors, ctx) =>
                        {
                            callOrder.Add("after");
                            return input;
                        }));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsAborted);
                    Assert.AreEqual(1, callOrder.Count);
                });

                builder.Add("Execute_WithNonFatalStage_ShouldContinueOnFailure", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    var callOrder = new List<string>();

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("nonFatal",
                        (input, errors, ctx) =>
                        {
                            callOrder.Add("nonFatal");
                            return null;
                        }) { IsFatal = false });

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("after",
                        (input, errors, ctx) =>
                        {
                            callOrder.Add("after");
                            return input;
                        }));

                    var result = pipeline.Execute("hello");

                    Assert.IsFalse(result.IsSuccess);
                    Assert.IsFalse(result.IsAborted);
                    Assert.AreEqual(2, callOrder.Count);
                });

                builder.Add("Execute_StageThrowsException_Fatal_ShouldAbort", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("thrower",
                        (input, errors, ctx) =>
                        {
                            throw new InvalidOperationException("test error");
                        }) { IsFatal = true });

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsAborted);
                    Assert.IsTrue(result.HasErrors);
                });

                builder.Add("Execute_StageThrowsException_NonFatal_ShouldContinue", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    var afterCalled = false;

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("thrower",
                        (input, errors, ctx) =>
                        {
                            throw new InvalidOperationException("test error");
                        }) { IsFatal = false });

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("after",
                        (input, errors, ctx) =>
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
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    string? capturedSource = null;

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("capture",
                        (input, errors, ctx) =>
                        {
                            capturedSource = ctx.SourceFile;
                            return input;
                        }));

                    pipeline.Execute("hello", "test.txt");

                    Assert.AreEqual("test.txt", capturedSource);
                });

                builder.Add("Clear_ShouldRemoveAllStages", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("stage1"));
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("stage2"));

                    Assert.AreEqual(2, pipeline.Stages.Count);
                    pipeline.Clear();
                    Assert.AreEqual(0, pipeline.Stages.Count);
                });

                builder.Add("Execute_EmptyPipeline_ShouldReturnSuccess", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual("hello", result.Data);
                });

                builder.Add("Execute_WithWarnings_ShouldReturnPartialSuccess", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    pipeline.RegisterStage(new MockPipelineStage<string, string>("warnStage",
                        (input, errors, ctx) =>
                        {
                            errors.Add("WARN1", "A warning", ErrorLevel.Warning);
                            return input;
                        }));

                    var result = pipeline.Execute("hello");

                    Assert.IsTrue(result.IsSuccess);
                    Assert.AreEqual(PipelineResultStatus.PartialSuccess, result.Status);
                });

                builder.Add("Execute_WithContext_ShouldUseProvidedContext", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    PipelineContext? capturedCtx = null;

                    pipeline.RegisterStage(new MockPipelineStage<string, string>("capture",
                        (input, errors, ctx) =>
                        {
                            capturedCtx = ctx;
                            return input;
                        }));

                    var ctx = new PipelineContext();
                    ctx.SetItem("key", "value");
                    pipeline.Execute("hello", ctx);

                    Assert.AreSame(ctx, capturedCtx);
                    Assert.AreEqual("value", capturedCtx?.GetItem<string>("key"));
                });

                builder.Add("Execute_WithCancelledContext_ShouldThrow", () =>
                {
                    var pipeline = new BasicMethod.Pipeline.Pipeline<string, string>();
                    var ctx = new PipelineContext();
                    ctx.Cancel();

                    Assert.ThrowsException<OperationCanceledException>(() =>
                        pipeline.Execute("hello", ctx));
                });
            });
            result.AssertAllPassed();
        }
    }
}
