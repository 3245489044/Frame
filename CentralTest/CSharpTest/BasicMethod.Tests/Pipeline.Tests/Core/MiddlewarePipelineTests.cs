using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class MiddlewarePipelineTests : TestBase
    {
        [TestMethod]
        public void RunAll_MiddlewarePipelineTests()
        {
            var result = RunTestGroup("MiddlewarePipeline", builder =>
            {
                builder.Add("Use_ShouldAddMiddleware", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    pipeline.Use(async (input, next, ctx) =>
                    {
                        return await next(input);
                    });

                    Assert.AreEqual(1, pipeline.MiddlewareCount);
                });

                builder.Add("Use_WithMultipleMiddleware_ShouldExecuteInOrder", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    var order = new List<string>();

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        order.Add("first_before");
                        var result = await next(input);
                        order.Add("first_after");
                        return result;
                    });

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        order.Add("second_before");
                        var result = await next(input);
                        order.Add("second_after");
                        return result;
                    });

                    pipeline.SetCore(input => input + "_core");
                    var output = pipeline.Execute("hello");

                    Assert.AreEqual("hello_core", output);
                    CollectionAssert.AreEqual(
                        new[] { "first_before", "second_before", "second_after", "first_after" },
                        order);
                });

                builder.Add("Use_WithContext_ShouldPassContext", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    PipelineContext? capturedCtx = null;

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        capturedCtx = ctx;
                        return await next(input);
                    });

                    pipeline.SetCore(input => input + "_core");
                    var ctx = new PipelineContext();
                    ctx.SetItem("key", "value");
                    pipeline.Execute("hello", ctx);

                    Assert.IsNotNull(capturedCtx);
                    Assert.AreEqual("value", capturedCtx?.GetItem<string>("key"));
                });

                builder.Add("Use_WithMiddlewareThatModifiesInput_ShouldAffectCore", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        return await next(input + "_modified");
                    });

                    pipeline.SetCore(input => input + "_core");
                    var output = pipeline.Execute("hello");

                    Assert.AreEqual("hello_modified_core", output);
                });

                builder.Add("Use_WithMiddlewareThatModifiesOutput_ShouldAffectResult", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        var result = await next(input);
                        return result + "_wrapped";
                    });

                    pipeline.SetCore(input => input + "_core");
                    var output = pipeline.Execute("hello");

                    Assert.AreEqual("hello_core_wrapped", output);
                });

                builder.Add("Use_WithShortCircuitMiddleware_ShouldSkipCore", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    var coreCalled = false;

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        return "short_circuited";
                    });

                    pipeline.SetCore(input =>
                    {
                        coreCalled = true;
                        return input + "_core";
                    });

                    var output = pipeline.Execute("hello");

                    Assert.AreEqual("short_circuited", output);
                    Assert.IsFalse(coreCalled);
                });

                builder.Add("Use_WithExceptionMiddleware_ShouldHandleError", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    var errorHandled = false;

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        try
                        {
                            return await next(input);
                        }
                        catch
                        {
                            errorHandled = true;
                            return "error_handled";
                        }
                    });

                    pipeline.SetCore(input => throw new InvalidOperationException("test error"));
                    var output = pipeline.Execute("hello");

                    Assert.IsTrue(errorHandled);
                    Assert.AreEqual("error_handled", output);
                });

                builder.Add("SetCore_ShouldSetCoreHandler", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    pipeline.SetCore(input => input + "_core");

                    var output = pipeline.Execute("hello");
                    Assert.AreEqual("hello_core", output);
                });

                builder.Add("SetCore_WithoutCore_ShouldThrow", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();

                    Assert.ThrowsException<InvalidOperationException>(() =>
                        pipeline.Execute("hello"));
                });

                builder.Add("Clear_ShouldRemoveAllMiddleware", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    pipeline.Use(async (input, next, ctx) => await next(input));
                    pipeline.Use(async (input, next, ctx) => await next(input));

                    Assert.AreEqual(2, pipeline.MiddlewareCount);
                    pipeline.Clear();
                    Assert.AreEqual(0, pipeline.MiddlewareCount);
                });

                builder.Add("MiddlewareCount_ShouldReflectCount", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();

                    Assert.AreEqual(0, pipeline.MiddlewareCount);
                    pipeline.Use(async (input, next, ctx) => await next(input));
                    Assert.AreEqual(1, pipeline.MiddlewareCount);
                    pipeline.Use(async (input, next, ctx) => await next(input));
                    Assert.AreEqual(2, pipeline.MiddlewareCount);
                });

                builder.Add("Execute_WithCancelledContext_ShouldThrow", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    pipeline.SetCore(input => input);
                    var ctx = new PipelineContext();
                    ctx.Cancel();

                    Assert.ThrowsException<OperationCanceledException>(() =>
                        pipeline.Execute("hello", ctx));
                });

                builder.Add("ExecuteAsync_ShouldWorkWithAsyncMiddleware", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();

                    pipeline.Use(async (input, next, ctx) =>
                    {
                        await Task.Yield();
                        return await next(input);
                    });

                    pipeline.SetCore(async input =>
                    {
                        await Task.Yield();
                        return input + "_async_core";
                    });

                    var output = pipeline.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_async_core", output);
                });
            });
            result.AssertAllPassed();
        }
    }
}
