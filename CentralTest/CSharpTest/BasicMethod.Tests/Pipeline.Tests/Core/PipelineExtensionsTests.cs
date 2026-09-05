using BasicMethod.Pipeline;
using CentralConsole.Testing;
using CentralConsole.Testing.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineExtensionsTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineExtensionsTests()
        {
            var result = RunTestGroup("PipelineExtensions", builder =>
            {
                builder.Add("Then_ShouldChainTwoPipelines", () =>
                {
                    var first = new DelegatePipeline<string, string>("first",
                        sync: input => input + "_first");
                    var second = new DelegatePipeline<string, string>("second",
                        sync: input => input + "_second");

                    var combined = first.Then(second);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("hello_first_second", output);
                });

                builder.Add("Then_WithTypeConversion_ShouldChainPipelines", () =>
                {
                    var first = new DelegatePipeline<string, int>("first",
                        sync: input => input.Length);
                    var second = new DelegatePipeline<int, string>("second",
                        sync: input => input.ToString());

                    var combined = first.Then(second);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("5", output);
                });

                builder.Add("Pipe_ShouldApplyFunction", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    var piped = pipeline.Pipe(input => input + "_piped");

                    var output = piped.Execute("hello");
                    Assert.AreEqual("hello_piped", output);
                });

                builder.Add("Pipe_WithContext_ShouldApplyFunction", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    var piped = pipeline.Pipe((input, ctx) =>
                    {
                        ctx.SetItem("piped", true);
                        return input + "_piped";
                    });

                    var ctx = new PipelineContext();
                    var output = piped.Execute("hello", ctx);

                    Assert.AreEqual("hello_piped", output);
                    Assert.IsTrue(ctx.GetItem<bool>("piped"));
                });

                builder.Add("Tap_ShouldExecuteSideEffect", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    string? captured = null;
                    var tapped = pipeline.Tap(input => captured = input);

                    var output = tapped.Execute("hello");

                    Assert.AreEqual("hello", output);
                    Assert.AreEqual("hello", captured);
                });

                builder.Add("Tap_WithContext_ShouldExecuteSideEffect", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    string? captured = null;
                    PipelineContext? capturedCtx = null;
                    var tapped = pipeline.Tap((input, ctx) =>
                    {
                        captured = input;
                        capturedCtx = ctx;
                    });

                    var ctx = new PipelineContext();
                    var output = tapped.Execute("hello", ctx);

                    Assert.AreEqual("hello", output);
                    Assert.AreEqual("hello", captured);
                    Assert.AreSame(ctx, capturedCtx);
                });

                builder.Add("Branch_ShouldSelectPipelineBasedOnCondition", () =>
                {
                    var truePipeline = new DelegatePipeline<string, string>("true",
                        sync: input => input + "_true");
                    var falsePipeline = new DelegatePipeline<string, string>("false",
                        sync: input => input + "_false");

                    var branched = PipelineExtensions.Branch(
                        input => input == "yes",
                        truePipeline,
                        falsePipeline);

                    Assert.AreEqual("yes_true", branched.Execute("yes"));
                    Assert.AreEqual("no_false", branched.Execute("no"));
                });

                builder.Add("Branch_WithContext_ShouldSelectPipelineBasedOnCondition", () =>
                {
                    var truePipeline = new DelegatePipeline<string, string>("true",
                        sync: input => input + "_true");
                    var falsePipeline = new DelegatePipeline<string, string>("false",
                        sync: input => input + "_false");

                    var branched = PipelineExtensions.Branch(
                        (input, ctx) => input == "yes",
                        truePipeline,
                        falsePipeline);

                    Assert.AreEqual("yes_true", branched.Execute("yes"));
                    Assert.AreEqual("no_false", branched.Execute("no"));
                });

                builder.Add("Retry_ShouldRetryOnFailure", () =>
                {
                    var attempts = 0;
                    var pipeline = new DelegatePipeline<string, string>("failing",
                        sync: input =>
                        {
                            attempts++;
                            if (attempts < 3)
                                throw new InvalidOperationException("fail");
                            return input + "_success";
                        });

                    var retried = pipeline.Retry(3);
                    var output = retried.Execute("hello");

                    Assert.AreEqual("hello_success", output);
                    Assert.AreEqual(3, attempts);
                });

                builder.Add("Retry_ShouldFailAfterMaxRetries", () =>
                {
                    var attempts = 0;
                    var pipeline = new DelegatePipeline<string, string>("alwaysFailing",
                        sync: input =>
                        {
                            attempts++;
                            throw new InvalidOperationException("always fail");
                        });

                    var retried = pipeline.Retry(2);

                    Assert.ThrowsException<InvalidOperationException>(() =>
                        retried.Execute("hello"));
                    Assert.AreEqual(3, attempts);
                });

                builder.Add("Timeout_ShouldThrowOnTimeout", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("slow",
                        async: async input =>
                        {
                            await Task.Delay(5000);
                            return input;
                        });

                    var timedOut = pipeline.Timeout(TimeSpan.FromMilliseconds(10));

                    Assert.ThrowsException<TimeoutException>(() =>
                        timedOut.Execute("hello"));
                });

                builder.Add("Log_ShouldCreateLoggingPipeline", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    var logged = pipeline.Log("TestLogger");

                    var output = logged.Execute("hello");
                    Assert.AreEqual("hello", output);
                });

                builder.Add("Generic_Then_ShouldChainTwoPipelines", () =>
                {
                    var first = new DelegatePipeline<string, string>("first",
                        sync: input => input + "_first");
                    var second = new DelegatePipeline<string, string>("second",
                        sync: input => input + "_second");

                    var combined = first.Then(second);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("hello_first_second", output);
                });

                builder.Add("Generic_Pipe_ShouldApplyFunction", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    var piped = pipeline.Pipe(input => input + "_piped");

                    var output = piped.Execute("hello");
                    Assert.AreEqual("hello_piped", output);
                });

                builder.Add("Generic_Tap_ShouldExecuteSideEffect", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    string? captured = null;
                    var tapped = pipeline.Tap(input => captured = input);

                    var output = tapped.Execute("hello");

                    Assert.AreEqual("hello", output);
                    Assert.AreEqual("hello", captured);
                });

                builder.Add("Generic_Retry_ShouldRetryOnFailure", () =>
                {
                    var attempts = 0;
                    var pipeline = new DelegatePipeline<string, string>("failing",
                        sync: input =>
                        {
                            attempts++;
                            if (attempts < 2)
                                throw new InvalidOperationException("fail");
                            return input + "_success";
                        });

                    var retried = pipeline.Retry(2);
                    var output = retried.Execute("hello");

                    Assert.AreEqual("hello_success", output);
                    Assert.AreEqual(2, attempts);
                });

                builder.Add("Generic_Timeout_ShouldThrowOnTimeout", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("slow",
                        async: async input =>
                        {
                            await Task.Delay(5000);
                            return input;
                        });

                    var timedOut = pipeline.Timeout(TimeSpan.FromMilliseconds(10));

                    Assert.ThrowsException<TimeoutException>(() =>
                        timedOut.Execute("hello"));
                });

                builder.Add("Generic_Log_ShouldCreateLoggingPipeline", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("base",
                        sync: input => input);
                    var logged = pipeline.Log("TestLogger");

                    var output = logged.Execute("hello");
                    Assert.AreEqual("hello", output);
                });
            });
            result.AssertAllPassed();
        }
    }
}
