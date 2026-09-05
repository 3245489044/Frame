using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class TryExecuteSkeletonTests : TestBase
    {
        [TestMethod]
        public void RunAll_MainPath_Success()
        {
            var result = RunTestGroup("TryExecuteSkeleton_MainPath", builder =>
            {
                builder.Add("Execute_MainSucceeds_ShouldReturnMainResult", () =>
                {
                    var main = new SyncSkeleton("main", _ => "mainResult");
                    var fallback = new SyncSkeleton("fallback", _ => "fallbackResult");
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    var output = skeleton.Execute("input", new SkeletonContext());
                    return output == "mainResult";
                });

                builder.Add("ExecuteAsync_MainSucceeds_ShouldReturnMainResult", () =>
                {
                    var main = new AsyncSkeleton("main", async _ =>
                    {
                        await Task.Delay(1);
                        return "mainResult";
                    });
                    var fallback = new AsyncSkeleton("fallback", async _ =>
                    {
                        await Task.Delay(1);
                        return "fallbackResult";
                    });
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    var output = skeleton.ExecuteAsync("input", new SkeletonContext()).Result;
                    return output == "mainResult";
                });

                builder.Add("Execute_MainSucceeds_FallbackShouldNotBeCalled", () =>
                {
                    bool fallbackCalled = false;
                    var main = new SyncSkeleton("main", _ => "mainResult");
                    var fallback = new SyncSkeleton("fallback", _ =>
                    {
                        fallbackCalled = true;
                        return "fallbackResult";
                    });
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    skeleton.Execute("input", new SkeletonContext());
                    return !fallbackCalled;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Fallback_OnException()
        {
            var result = RunTestGroup("TryExecuteSkeleton_Fallback", builder =>
            {
                builder.Add("Execute_MainThrows_ShouldReturnFallbackResult", () =>
                {
                    var main = new SyncSkeleton("main", _ => throw new InvalidOperationException("main failed"));
                    var fallback = new SyncSkeleton("fallback", _ => "fallbackResult");
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    var output = skeleton.Execute("input", new SkeletonContext());
                    return output == "fallbackResult";
                });

                builder.Add("ExecuteAsync_MainThrows_ShouldReturnFallbackResult", () =>
                {
                    var main = new AsyncSkeleton("main", async _ =>
                    {
                        await Task.Delay(1);
                        throw new InvalidOperationException("async main failed");
                    });
                    var fallback = new AsyncSkeleton("fallback", async _ =>
                    {
                        await Task.Delay(1);
                        return "fallbackResult";
                    });
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    var output = skeleton.ExecuteAsync("input", new SkeletonContext()).Result;
                    return output == "fallbackResult";
                });

                builder.Add("Execute_FallbackThrows_ShouldPropagateException", () =>
                {
                    var main = new SyncSkeleton("main", _ => throw new InvalidOperationException("main failed"));
                    var fallback = new SyncSkeleton("fallback", _ => throw new InvalidOperationException("fallback also failed"));
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    try
                    {
                        skeleton.Execute("input", new SkeletonContext());
                        return false;
                    }
                    catch (InvalidOperationException ex)
                    {
                        return ex.Message == "fallback also failed";
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ExceptionInfo()
        {
            var result = RunTestGroup("TryExecuteSkeleton_ExceptionInfo", builder =>
            {
                builder.Add("Execute_MainThrows_ShouldRecordExceptionInContext", () =>
                {
                    var main = new SyncSkeleton("main", _ => throw new InvalidOperationException("main error"));
                    var fallback = new SyncSkeleton("fallback", ctx =>
                    {
                        var exInfo = ctx.GetData<ExceptionInfo>("LastException");
                        return exInfo != null ? "exceptionRecorded" : "noException";
                    });
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    var output = skeleton.Execute("input", new SkeletonContext());
                    return output == "exceptionRecorded";
                });

                builder.Add("ExceptionInfo_ShouldContainCorrectDetails", () =>
                {
                    var main = new SyncSkeleton("main", _ => throw new InvalidOperationException("specific error"));
                    ExceptionInfo? capturedInfo = null;
                    var fallback = new SyncSkeleton("fallback", ctx =>
                    {
                        capturedInfo = ctx.GetData<ExceptionInfo>("LastException");
                        return "fallback";
                    });
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback);

                    skeleton.Execute("input", new SkeletonContext());
                    return capturedInfo != null
                        && capturedInfo.Exception.Message == "specific error"
                        && capturedInfo.Exception is InvalidOperationException
                        && capturedInfo.StepName == "main";
                });

                builder.Add("ExceptionInfo_ToString_ShouldContainTypeAndMessage", () =>
                {
                    var ex = new InvalidOperationException("test error");
                    var info = new ExceptionInfo(ex) { StepName = "step1" };
                    var str = info.ToString();
                    return str.Contains("InvalidOperationException")
                        && str.Contains("test error");
                });

                builder.Add("ExceptionInfo_NullException_ShouldThrow", () =>
                {
                    try
                    {
                        var info = new ExceptionInfo(null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ConstructorValidation()
        {
            var result = RunTestGroup("TryExecuteSkeleton_Constructor", builder =>
            {
                builder.Add("Constructor_NullMain_ShouldThrow", () =>
                {
                    var fallback = new SyncSkeleton("fallback", _ => "result");
                    try
                    {
                        var skeleton = new TryExecuteSkeleton<string, string>(null!, fallback);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("Constructor_NullFallback_ShouldThrow", () =>
                {
                    var main = new SyncSkeleton("main", _ => "result");
                    try
                    {
                        var skeleton = new TryExecuteSkeleton<string, string>(main, null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("Constructor_WithCustomName_ShouldUseIt", () =>
                {
                    var main = new SyncSkeleton("main", _ => "result");
                    var fallback = new SyncSkeleton("fallback", _ => "result");
                    var skeleton = new TryExecuteSkeleton<string, string>(main, fallback, "CustomTryExecute");
                    var name = skeleton.GetType().GetProperty("Name")?.GetValue(skeleton) as string;
                    return name == "CustomTryExecute";
                });
            });

            result.AssertAllPassed();
        }

        private class SyncSkeleton : SkeletonBase<string, string>
        {
            private readonly Func<SkeletonContext, string> _func;

            public SyncSkeleton(string name, Func<SkeletonContext, string> func)
                : base(name, $"Sync skeleton '{name}'")
            {
                _func = func;
            }

            protected override string ExecuteCore(string input, SkeletonContext ctx)
                => _func(ctx);

            protected override Task<string> ExecuteCoreAsync(string input, SkeletonContext ctx)
                => Task.FromResult(ExecuteCore(input, ctx));
        }

        private class AsyncSkeleton : SkeletonBase<string, string>
        {
            private readonly Func<SkeletonContext, Task<string>> _func;

            public AsyncSkeleton(string name, Func<SkeletonContext, Task<string>> func)
                : base(name, $"Async skeleton '{name}'")
            {
                _func = func;
            }

            protected override string ExecuteCore(string input, SkeletonContext ctx)
                => ExecuteCoreAsync(input, ctx).GetAwaiter().GetResult();

            protected override Task<string> ExecuteCoreAsync(string input, SkeletonContext ctx)
                => _func(ctx);
        }
    }
}
