using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Handlers
{
    [TestClass]
    public class StructureHandlerTests : TestBase
    {
        [TestMethod]
        public void RunAll_StructureHandlerTests()
        {
            var result = RunTestGroup("StructureHandler", builder =>
            {
                builder.Add("Constructor_ShouldCreateHandler", () =>
                {
                    var handler = new StructureHandler<string, string>("test",
                        sync: input => input + "_processed");

                    Assert.AreEqual("test", handler.Name);
                });

                builder.Add("Execute_ShouldProcessInput", () =>
                {
                    var handler = new StructureHandler<string, string>("test",
                        sync: input => input + "_processed");

                    var output = handler.Execute("hello");
                    Assert.AreEqual("hello_processed", output);
                });

                builder.Add("ExecuteAsync_ShouldProcessInputAsync", () =>
                {
                    var handler = new StructureHandler<string, string>("test",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input + "_async";
                        });

                    var output = handler.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_async", output);
                });

                builder.Add("Execute_WithContext_ShouldPassContext", () =>
                {
                    PipelineContext? capturedCtx = null;
                    var handler = new StructureHandler<string, string>("test",
                        syncWithCtx: (input, ctx) =>
                        {
                            capturedCtx = ctx;
                            return input + "_ctx";
                        });

                    var ctx = new PipelineContext();
                    ctx.SetItem("key", "value");
                    var output = handler.Execute("hello", ctx);

                    Assert.AreEqual("hello_ctx", output);
                    Assert.IsNotNull(capturedCtx);
                    Assert.AreEqual("value", capturedCtx?.GetItem<string>("key"));
                });

                builder.Add("ExecuteAsync_WithContext_ShouldPassContext", () =>
                {
                    PipelineContext? capturedCtx = null;
                    var handler = new StructureHandler<string, string>("test",
                        asyncWithCtx: async (input, ctx) =>
                        {
                            await Task.Yield();
                            capturedCtx = ctx;
                            return input + "_async_ctx";
                        });

                    var ctx = new PipelineContext();
                    var output = handler.ExecuteAsync("hello", ctx).Result;

                    Assert.AreEqual("hello_async_ctx", output);
                    Assert.IsNotNull(capturedCtx);
                });

                builder.Add("Execute_WithoutSyncDelegate_ShouldThrow", () =>
                {
                    var handler = new StructureHandler<string, string>("test",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input;
                        });

                    Assert.ThrowsException<NotSupportedException>(() =>
                        handler.Execute("hello"));
                });

                builder.Add("ExecuteAsync_WithoutAsyncDelegate_ShouldFallbackToSync", () =>
                {
                    var handler = new StructureHandler<string, string>("test",
                        sync: input => input + "_sync");

                    var output = handler.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_sync", output);
                });

                builder.Add("Constructor_WithoutAnyDelegate_ShouldThrow", () =>
                {
                    Assert.ThrowsException<ArgumentException>(() =>
                        new StructureHandler<string, string>("empty"));
                });

                builder.Add("Name_ShouldDefaultToTypeName", () =>
                {
                    var handler = new StructureHandler<string, string>(sync: input => input);
                    Assert.AreEqual("StructureHandler", handler.Name);
                });

                builder.Add("Description_ShouldBeSettable", () =>
                {
                    var handler = new StructureHandler<string, string>("test", "My desc",
                        sync: input => input);
                    Assert.AreEqual("My desc", handler.Description);
                });

                builder.Add("Execute_WithCancelledContext_ShouldThrow", () =>
                {
                    var handler = new StructureHandler<string, string>("test",
                        sync: input => input);
                    var ctx = new PipelineContext();
                    ctx.Cancel();

                    Assert.ThrowsException<OperationCanceledException>(() =>
                        handler.Execute("hello", ctx));
                });
            });
            result.AssertAllPassed();
        }
    }
}
