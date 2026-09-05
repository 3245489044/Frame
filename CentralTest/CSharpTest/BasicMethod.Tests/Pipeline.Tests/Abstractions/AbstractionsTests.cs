using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Abstractions
{
    [TestClass]
    public class AbstractionsTests : TestBase
    {
        [TestMethod]
        public void RunAll_AbstractionsTests()
        {
            var result = RunTestGroup("Abstractions", builder =>
            {
                builder.Add("IPipeline_Execute_ShouldReturnResult", () =>
                {
                    IPipeline pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input + "_processed");

                    var result = pipeline.Execute("hello");
                    Assert.AreEqual("hello_processed", result);
                });

                builder.Add("IPipeline_ExecuteAsync_ShouldReturnTask", () =>
                {
                    IPipeline pipeline = new DelegatePipeline<string, string>("test",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input + "_async";
                        });

                    var result = pipeline.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_async", result);
                });

                builder.Add("IPipeline_Name_ShouldBeAccessible", () =>
                {
                    IPipeline pipeline = new DelegatePipeline<string, string>("MyPipeline");
                    Assert.AreEqual("MyPipeline", pipeline.Name);
                });

                builder.Add("IPipeline_Description_ShouldBeAccessible", () =>
                {
                    IPipeline pipeline = new DelegatePipeline<string, string>("test", description: "My desc");
                    Assert.AreEqual("My desc", pipeline.Description);
                });

                builder.Add("IPipeline_Typed_Execute_ShouldReturnTypedResult", () =>
                {
                    IPipeline<string, string> pipeline = new DelegatePipeline<string, string>("test",
                        sync: input => input + "_typed");

                    var result = pipeline.Execute("hello");
                    Assert.AreEqual("hello_typed", result);
                });

                builder.Add("IPipeline_Typed_ExecuteAsync_ShouldReturnTask", () =>
                {
                    IPipeline<string, string> pipeline = new DelegatePipeline<string, string>("test",
                        async: async input =>
                        {
                            await Task.Yield();
                            return input + "_typed_async";
                        });

                    var result = pipeline.ExecuteAsync("hello").Result;
                    Assert.AreEqual("hello_typed_async", result);
                });

                builder.Add("IPipelineStage_Process_ShouldReturnResult", () =>
                {
                    var stage = new TestStage("test", (input, errors, ctx) => input?.ToString()?.ToUpper());

                    var result = stage.Process("hello", new ErrorCollector(), new PipelineContext());
                    Assert.AreEqual("HELLO", result);
                });

                builder.Add("IPipelineStage_Name_ShouldBeAccessible", () =>
                {
                    var stage = new TestStage("MyStage", (input, errors, ctx) => input);
                    Assert.AreEqual("MyStage", stage.Name);
                });

                builder.Add("IPipelineStage_Order_ShouldBeSettable", () =>
                {
                    var stage = new TestStage("test", (input, errors, ctx) => input) { Order = 5 };
                    Assert.AreEqual(5, stage.Order);
                });

                builder.Add("IPipelineStage_IsFatal_ShouldBeSettable", () =>
                {
                    var stage = new TestStage("test", (input, errors, ctx) => input) { IsFatal = true };
                    Assert.IsTrue(stage.IsFatal);
                });

                builder.Add("IPipelineStage_Typed_Process_ShouldReturnTypedResult", () =>
                {
                    var stage = new TestStage<string, string>("test",
                        (input, errors, ctx) => input.ToUpper());

                    var result = stage.Process("hello", new ErrorCollector(), new PipelineContext());
                    Assert.AreEqual("HELLO", result);
                });

                builder.Add("IExecutionStrategy_Execute_ShouldProcessStages", () =>
                {
                    IExecutionStrategy strategy = new SequentialExecutionStrategy();
                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", (input, errors, ctx) => input?.ToString()?.ToUpper())
                    };

                    var errors = new ErrorCollector();
                    var ctx = new PipelineContext();
                    strategy.Execute(stages, "hello", errors, ctx);

                    Assert.IsFalse(errors.HasErrors);
                });

                builder.Add("PipelineResultStatus_Values_ShouldBeCorrect", () =>
                {
                    Assert.AreEqual(0, (int)PipelineResultStatus.Success);
                    Assert.AreEqual(1, (int)PipelineResultStatus.PartialSuccess);
                    Assert.AreEqual(2, (int)PipelineResultStatus.Failed);
                    Assert.AreEqual(3, (int)PipelineResultStatus.Aborted);
                });

                builder.Add("ErrorLevel_Values_ShouldBeCorrect", () =>
                {
                    Assert.AreEqual(0, (int)ErrorLevel.Info);
                    Assert.AreEqual(1, (int)ErrorLevel.Warning);
                    Assert.AreEqual(2, (int)ErrorLevel.Error);
                    Assert.AreEqual(3, (int)ErrorLevel.Fatal);
                });
            });
            result.AssertAllPassed();
        }

        private class TestStage : IPipelineStage
        {
            private readonly Func<object?, ErrorCollector, PipelineContext, object?> _processFunc;

            public string Name { get; }
            public int Order { get; set; }
            public bool IsFatal { get; set; }

            public TestStage(string name, Func<object?, ErrorCollector, PipelineContext, object?> processFunc)
            {
                Name = name;
                _processFunc = processFunc;
            }

            public object? Process(object input, ErrorCollector errors, PipelineContext context)
                => _processFunc(input, errors, context);
        }

        private class TestStage<TInput, TOutput> : IPipelineStage<TInput, TOutput>
            where TOutput : class
        {
            private readonly Func<TInput, ErrorCollector, PipelineContext, TOutput> _processFunc;

            public string Name { get; }
            public int Order { get; set; }
            public bool IsFatal { get; set; }

            public TestStage(string name, Func<TInput, ErrorCollector, PipelineContext, TOutput> processFunc)
            {
                Name = name;
                _processFunc = processFunc;
            }

            public TOutput? Process(TInput input, ErrorCollector errors, PipelineContext context)
                => _processFunc(input, errors, context);
        }
    }
}
