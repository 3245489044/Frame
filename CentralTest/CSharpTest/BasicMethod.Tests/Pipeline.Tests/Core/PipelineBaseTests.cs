using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineBaseTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineBaseTests()
        {
            var result = RunTestGroup("PipelineBase", builder =>
            {
                builder.Add("Execute_ShouldCallExecuteCore", () =>
                {
                    var pipeline = new TestPipeline("test");
                    var result = pipeline.Execute("hello");

                    Assert.AreEqual("hello_processed", result);
                    Assert.IsTrue(pipeline.SyncCalled);
                });

                builder.Add("ExecuteAsync_ShouldCallExecuteCoreAsync", () =>
                {
                    var pipeline = new TestPipeline("test");
                    var result = pipeline.ExecuteAsync("hello").Result;

                    Assert.AreEqual("hello_async", result);
                    Assert.IsTrue(pipeline.AsyncCalled);
                });

                builder.Add("Execute_WithContext_ShouldUseProvidedContext", () =>
                {
                    var pipeline = new TestPipeline("test");
                    var ctx = new PipelineContext();
                    ctx.SetItem("key", "value");

                    pipeline.Execute("hello", ctx);

                    Assert.AreSame(ctx, pipeline.LastContext);
                    Assert.AreEqual("value", pipeline.LastContext?.GetItem<string>("key"));
                });

                builder.Add("Execute_WithoutContext_ShouldCreateNewContext", () =>
                {
                    var pipeline = new TestPipeline("test");
                    pipeline.Execute("hello");

                    Assert.IsNotNull(pipeline.LastContext);
                    Assert.AreNotEqual(string.Empty, pipeline.LastContext?.Id);
                });

                builder.Add("Execute_WithCancelledContext_ShouldThrow", () =>
                {
                    var pipeline = new TestPipeline("test");
                    var ctx = new PipelineContext();
                    ctx.Cancel();

                    Assert.ThrowsException<OperationCanceledException>(() =>
                        pipeline.Execute("hello", ctx));
                });

                builder.Add("ExecuteAsync_WithCancelledContext_ShouldThrow", () =>
                {
                    var pipeline = new TestPipeline("test");
                    var ctx = new PipelineContext();
                    ctx.Cancel();

                    Assert.ThrowsException<OperationCanceledException>(() =>
                        pipeline.ExecuteAsync("hello", ctx).GetAwaiter().GetResult());
                });

                builder.Add("Name_ShouldDefaultToTypeName", () =>
                {
                    var pipeline = new TestPipeline();
                    Assert.AreEqual("TestPipeline", pipeline.Name);
                });

                builder.Add("Name_ShouldUseProvidedName", () =>
                {
                    var pipeline = new TestPipeline("MyPipeline");
                    Assert.AreEqual("MyPipeline", pipeline.Name);
                });

                builder.Add("Description_ShouldBeSettable", () =>
                {
                    var pipeline = new TestPipeline("test", "My description");
                    Assert.AreEqual("My description", pipeline.Description);
                });

                builder.Add("Execute_ContextDisposedAfterExecution", () =>
                {
                    var pipeline = new TestPipeline("test");
                    PipelineContext? capturedCtx = null;

                    var trackingPipeline = new TrackingPipeline("tracking", ctx => capturedCtx = ctx);
                    trackingPipeline.Execute("hello");

                    Assert.IsNotNull(capturedCtx);
                    Assert.ThrowsException<ObjectDisposedException>(() => capturedCtx!.Cancel());
                });
            });
            result.AssertAllPassed();
        }

        private class TestPipeline : PipelineBase<string, string>
        {
            public bool SyncCalled { get; private set; }
            public bool AsyncCalled { get; private set; }
            public PipelineContext? LastContext { get; private set; }

            public TestPipeline(string? name = null, string? description = null)
                : base(name, description) { }

            protected override string ExecuteCore(string input, PipelineContext context)
            {
                SyncCalled = true;
                LastContext = context;
                return input + "_processed";
            }

            protected override Task<string> ExecuteCoreAsync(string input, PipelineContext context)
            {
                AsyncCalled = true;
                LastContext = context;
                return Task.FromResult(input + "_async");
            }
        }

        private class TrackingPipeline : PipelineBase<string, string>
        {
            private readonly Action<PipelineContext> _capture;

            public TrackingPipeline(string name, Action<PipelineContext> capture)
                : base(name) => _capture = capture;

            protected override string ExecuteCore(string input, PipelineContext context)
            {
                _capture(context);
                return input;
            }

            protected override Task<string> ExecuteCoreAsync(string input, PipelineContext context)
            {
                _capture(context);
                return Task.FromResult(input);
            }
        }
    }
}
