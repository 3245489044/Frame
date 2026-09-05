using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class GuardTests : TestBase
    {
        [TestMethod]
        public void RunAll_GuardTests()
        {
            var result = RunTestGroup("Guard", builder =>
            {
                builder.Add("Guard_WithTrueCondition_ShouldPass", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(input => true);
                    var output = guarded.Execute("hello");

                    Assert.AreEqual("hello", output);
                });

                builder.Add("Guard_WithFalseCondition_ShouldThrow", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(input => false);

                    Assert.ThrowsException<PipelineGuardException>(() =>
                        guarded.Execute("hello"));
                });

                builder.Add("Guard_WithCustomErrorMessage_ShouldUseIt", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(input => false, "Custom guard message");

                    var ex = Assert.ThrowsException<PipelineGuardException>(() =>
                        guarded.Execute("hello"));
                    Assert.AreEqual("Custom guard message", ex.Message);
                });

                builder.Add("Guard_WithContextCondition_ShouldPassContext", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    PipelineContext? capturedCtx = null;
                    var guarded = pipeline.Guard((input, ctx) =>
                    {
                        capturedCtx = ctx;
                        return true;
                    });

                    var ctx = new PipelineContext();
                    ctx.SetItem("key", "value");
                    guarded.Execute("hello", ctx);

                    Assert.IsNotNull(capturedCtx);
                    Assert.AreEqual("value", capturedCtx?.GetItem<string>("key"));
                });

                builder.Add("Guard_WithAsyncCondition_ShouldWork", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(async input =>
                    {
                        await Task.Yield();
                        return input == "hello";
                    });

                    var output = guarded.Execute("hello");
                    Assert.AreEqual("hello", output);
                });

                builder.Add("Guard_WithAsyncFalseCondition_ShouldThrow", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(async input =>
                    {
                        await Task.Yield();
                        return false;
                    });

                    Assert.ThrowsException<PipelineGuardException>(() =>
                        guarded.Execute("hello"));
                });

                builder.Add("Guard_WithAsyncContextCondition_ShouldWork", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(async (input, ctx) =>
                    {
                        await Task.Yield();
                        return ctx.HasItem("allowed");
                    });

                    var ctx = new PipelineContext();
                    ctx.SetItem("allowed", true);
                    var output = guarded.Execute("hello", ctx);

                    Assert.AreEqual("hello", output);
                });

                builder.Add("Guard_WithAsyncContextFalse_ShouldThrow", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input);

                    var guarded = pipeline.Guard(async (input, ctx) =>
                    {
                        await Task.Yield();
                        return false;
                    });

                    Assert.ThrowsException<PipelineGuardException>(() =>
                        guarded.Execute("hello"));
                });

                builder.Add("PipelineGuardException_ShouldBeCorrectType", () =>
                {
                    var ex = new PipelineGuardException("test message");

                    Assert.IsInstanceOfType(ex, typeof(Exception));
                    Assert.AreEqual("test message", ex.Message);
                });
            });
            result.AssertAllPassed();
        }
    }
}
