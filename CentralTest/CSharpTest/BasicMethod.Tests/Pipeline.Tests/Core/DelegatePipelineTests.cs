using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class DelegatePipelineTests : TestBase
    {
        [TestMethod]
        public void RunAll_DelegatePipelineTests()
        {
            var result = RunTestGroup("DelegatePipeline", builder =>
            {
                builder.Add("Constructor_WithSyncDelegate_ShouldExecuteSync", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        sync: input => input + "_sync");

                    var result = pipeline.Execute("hello");
                    Assert.AreEqual("hello_sync", result);
                });

                builder.Add("Constructor_WithAsyncDelegate_ShouldExecuteAsync", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input + "_async";
                        });

                    var result = pipeline.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_async", result);
                });

                builder.Add("Constructor_WithBothDelegates_ShouldUseCorrectOne", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        sync: input => input + "_sync",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input + "_async";
                        });

                    Assert.AreEqual("hello_sync", pipeline.Execute("hello"));
                    Assert.AreEqual("hello_async", pipeline.ExecuteAsync("hello").Result);
                });

                builder.Add("Constructor_WithContextDelegates_ShouldPassContext", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        syncWithCtx: (input, ctx) =>
                        {
                            ctx.SetItem("processed", true);
                            return input + "_ctx";
                        });

                    var ctx = new PipelineContext();
                    var result = pipeline.Execute("hello", ctx);

                    Assert.AreEqual("hello_ctx", result);
                    Assert.IsTrue(ctx.GetItem<bool>("processed"));
                });

                builder.Add("Constructor_WithAsyncContextDelegate_ShouldPassContext", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        asyncWithCtx: async (input, ctx) =>
                        {
                            await Task.Yield();
                            ctx.SetItem("processed", true);
                            return input + "_async_ctx";
                        });

                    var ctx = new PipelineContext();
                    var result = pipeline.ExecuteAsync("hello", ctx).Result;

                    Assert.AreEqual("hello_async_ctx", result);
                    Assert.IsTrue(ctx.GetItem<bool>("processed"));
                });

                builder.Add("Execute_WithoutSyncDelegate_ShouldThrow", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input;
                        });

                    Assert.ThrowsException<NotSupportedException>(() =>
                        pipeline.Execute("hello"));
                });

                builder.Add("ExecuteAsync_WithoutAsyncDelegate_ShouldFallbackToSync", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "test",
                        sync: input => input + "_sync");

                    var result = pipeline.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_sync", result);
                });

                builder.Add("Constructor_WithoutAnyDelegate_ShouldThrow", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("empty");

                    Assert.ThrowsException<NotSupportedException>(() =>
                        pipeline.Execute("hello"));
                });

                builder.Add("Constructor_WithSimpleDelegates_ShouldWork", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>(
                        "simple",
                        sync: input => input,
                        async: async input =>
                        {
                            await Task.Yield();
                            return input;
                        },
                        description: "Simple pipeline");

                    Assert.AreEqual("simple", pipeline.Name);
                    Assert.AreEqual("Simple pipeline", pipeline.Description);
                    Assert.AreEqual("hello", pipeline.Execute("hello"));
                });

                builder.Add("Name_ShouldBeSettable", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("MyPipe");
                    Assert.AreEqual("MyPipe", pipeline.Name);
                });

                builder.Add("Name_ShouldDefaultToTypeName", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>();
                    Assert.AreEqual("DelegatePipeline", pipeline.Name);
                });
            });
            result.AssertAllPassed();
        }
    }
}
