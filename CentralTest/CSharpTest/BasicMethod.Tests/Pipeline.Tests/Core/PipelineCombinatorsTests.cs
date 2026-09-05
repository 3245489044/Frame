using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineCombinatorsTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineCombinatorsTests()
        {
            var result = RunTestGroup("PipelineCombinators", builder =>
            {
                builder.Add("Combine_ShouldMergeTwoPipelines", () =>
                {
                    var first = new DelegatePipeline<string, string>("first",
                        sync: input => input + "_first");
                    var second = new DelegatePipeline<string, string>("second",
                        sync: input => input + "_second");

                    var combined = PipelineCombinators.Combine(first, second);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("hello_first_second", output);
                });

                builder.Add("Combine_WithDifferentTypes_ShouldChainCorrectly", () =>
                {
                    var first = new DelegatePipeline<string, int>("first",
                        sync: input => input.Length);
                    var second = new DelegatePipeline<int, string>("second",
                        sync: input => input.ToString());

                    var combined = PipelineCombinators.Combine(first, second);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("5", output);
                });

                builder.Add("Combine_WithMultiplePipelines_ShouldChainAll", () =>
                {
                    var first = new DelegatePipeline<string, string>("first",
                        sync: input => input + "_1");
                    var second = new DelegatePipeline<string, string>("second",
                        sync: input => input + "_2");
                    var third = new DelegatePipeline<string, string>("third",
                        sync: input => input + "_3");

                    var combined = PipelineCombinators.Combine(first, second, third);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("hello_1_2_3", output);
                });

                builder.Add("Combine_WithEmptyList_ShouldThrow", () =>
                {
                    Assert.ThrowsException<ArgumentException>(() =>
                        PipelineCombinators.Combine<string, string>());
                });

                builder.Add("Combine_WithSinglePipeline_ShouldReturnSame", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("single",
                        sync: input => input + "_single");

                    var combined = PipelineCombinators.Combine(pipeline);
                    var output = combined.Execute("hello");

                    Assert.AreEqual("hello_single", output);
                });

                builder.Add("Race_ShouldReturnFirstCompleted", () =>
                {
                    var slow = new DelegatePipeline<string, string>("slow",
                        async: async input =>
                        {
                            await Task.Delay(1000);
                            return input + "_slow";
                        });

                    var fast = new DelegatePipeline<string, string>("fast",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input + "_fast";
                        });

                    var raced = PipelineCombinators.Race(slow, fast);
                    var output = raced.Execute("hello");

                    Assert.AreEqual("hello_fast", output);
                });

                builder.Add("Race_WithSinglePipeline_ShouldReturnSame", () =>
                {
                    var pipeline = new DelegatePipeline<string, string>("single",
                        sync: input => input);

                    var raced = PipelineCombinators.Race(pipeline);
                    var output = raced.Execute("hello");

                    Assert.AreEqual("hello", output);
                });

                builder.Add("Race_WithEmptyList_ShouldThrow", () =>
                {
                    Assert.ThrowsException<ArgumentException>(() =>
                        PipelineCombinators.Race<string, string>());
                });

                builder.Add("First_ShouldReturnFirstPipelineResult", () =>
                {
                    var first = new DelegatePipeline<string, string>("first",
                        sync: input => input + "_first");
                    var second = new DelegatePipeline<string, string>("second",
                        sync: input => input + "_second");

                    var result = PipelineCombinators.First(first, second);
                    var output = result.Execute("hello");

                    Assert.AreEqual("hello_first", output);
                });

                builder.Add("First_WithEmptyList_ShouldThrow", () =>
                {
                    Assert.ThrowsException<ArgumentException>(() =>
                        PipelineCombinators.First<string, string>());
                });

                builder.Add("Last_ShouldReturnLastPipelineResult", () =>
                {
                    var first = new DelegatePipeline<string, string>("first",
                        sync: input => input + "_first");
                    var second = new DelegatePipeline<string, string>("second",
                        sync: input => input + "_second");

                    var result = PipelineCombinators.Last(first, second);
                    var output = result.Execute("hello");

                    Assert.AreEqual("hello_second", output);
                });

                builder.Add("Last_WithEmptyList_ShouldThrow", () =>
                {
                    Assert.ThrowsException<ArgumentException>(() =>
                        PipelineCombinators.Last<string, string>());
                });

                builder.Add("Fallback_ShouldUsePrimaryOnSuccess", () =>
                {
                    var primary = new DelegatePipeline<string, string>("primary",
                        sync: input => input + "_primary");
                    var fallback = new DelegatePipeline<string, string>("fallback",
                        sync: input => input + "_fallback");

                    var result = PipelineCombinators.Fallback(primary, fallback);
                    var output = result.Execute("hello");

                    Assert.AreEqual("hello_primary", output);
                });

                builder.Add("Fallback_ShouldUseFallbackOnFailure", () =>
                {
                    var primary = new DelegatePipeline<string, string>("primary",
                        sync: input => throw new InvalidOperationException("fail"));
                    var fallback = new DelegatePipeline<string, string>("fallback",
                        sync: input => input + "_fallback");

                    var result = PipelineCombinators.Fallback(primary, fallback);
                    var output = result.Execute("hello");

                    Assert.AreEqual("hello_fallback", output);
                });

                builder.Add("Fallback_WithMultipleFallbacks_ShouldTryAll", () =>
                {
                    var primary = new DelegatePipeline<string, string>("primary",
                        sync: input => throw new InvalidOperationException("fail1"));
                    var fallback1 = new DelegatePipeline<string, string>("fallback1",
                        sync: input => throw new InvalidOperationException("fail2"));
                    var fallback2 = new DelegatePipeline<string, string>("fallback2",
                        sync: input => input + "_fallback2");

                    var result = PipelineCombinators.Fallback(primary, fallback1, fallback2);
                    var output = result.Execute("hello");

                    Assert.AreEqual("hello_fallback2", output);
                });

                builder.Add("Fallback_AllFail_ShouldThrow", () =>
                {
                    var primary = new DelegatePipeline<string, string>("primary",
                        sync: input => throw new InvalidOperationException("fail1"));
                    var fallback = new DelegatePipeline<string, string>("fallback",
                        sync: input => throw new InvalidOperationException("fail2"));

                    var result = PipelineCombinators.Fallback(primary, fallback);

                    Assert.ThrowsException<AggregateException>(() =>
                        result.Execute("hello"));
                });
            });
            result.AssertAllPassed();
        }
    }
}
