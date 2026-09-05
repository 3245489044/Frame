using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using BasicMethod.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class MiddlewarePipelineTests : TestBase
    {
        [TestMethod]
        public void RunAll_Registration()
        {
            var result = RunTestGroup("MiddlewarePipeline_Registration", builder =>
            {
                builder.Add("Use_ShouldAddMiddleware", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, object>();
                    pipeline.Use((MiddlewareDelegate<string, object>)(async (request, next) => await next(request)));
                    return pipeline.MiddlewareCount == 1;
                });

                builder.Add("Use_NullMiddleware_ShouldThrow", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, object>();
                    try
                    {
                        pipeline.Use((MiddlewareDelegate<string, object>)null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("Use_Multiple_ShouldAddAll", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, object>();
                    pipeline.Use((MiddlewareDelegate<string, object>)(async (request, next) => await next(request)));
                    pipeline.Use((MiddlewareDelegate<string, object>)(async (request, next) => await next(request)));
                    pipeline.Use((MiddlewareDelegate<string, object>)(async (request, next) => await next(request)));
                    return pipeline.MiddlewareCount == 3;
                });

                builder.Add("Clear_ShouldRemoveAll", () =>
                {
                    var pipeline = new MiddlewarePipeline<string, object>();
                    pipeline.Use((MiddlewareDelegate<string, object>)(async (request, next) => await next(request)));
                    pipeline.Use((MiddlewareDelegate<string, object>)(async (request, next) => await next(request)));
                    pipeline.Clear();
                    return pipeline.MiddlewareCount == 0;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_Execution()
        {
            var result = await RunTestGroupAsync("MiddlewarePipeline_Execution", builder =>
            {
                builder.Add("ExecuteAsync_ShouldProcessThroughAllMiddlewares", async () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    pipeline.Use((MiddlewareDelegate<string, string>)(async (request, next) =>
                    {
                        var result = await next(request);
                        return result + "!";
                    }));
                    pipeline.Use((MiddlewareDelegate<string, string>)(async (request, next) =>
                    {
                        var result = await next(request);
                        return result + "?";
                    }));
                    pipeline.SetCoreHandler(request => Task.FromResult(request));
                    var output = await pipeline.ExecuteAsync("hello");
                    return output == "hello?!";
                });

                builder.Add("ExecuteAsync_EmptyPipeline_ShouldThrow", async () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    try
                    {
                        await pipeline.ExecuteAsync("input");
                        return false;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public async Task RunAll_ExceptionHandling()
        {
            var result = await RunTestGroupAsync("MiddlewarePipeline_ExceptionHandling", builder =>
            {
                builder.Add("ExecuteAsync_MiddlewareThrows_ShouldPropagate", async () =>
                {
                    var pipeline = new MiddlewarePipeline<string, string>();
                    pipeline.Use((MiddlewareDelegate<string, string>)(async (request, next) =>
                    {
                        throw new InvalidOperationException("oops");
                    }));
                    pipeline.SetCoreHandler(request => Task.FromResult(request));
                    try
                    {
                        await pipeline.ExecuteAsync("input");
                        return false;
                    }
                    catch (InvalidOperationException ex)
                    {
                        return ex.Message == "oops";
                    }
                });
            });

            result.AssertAllPassed();
        }
    }
}
