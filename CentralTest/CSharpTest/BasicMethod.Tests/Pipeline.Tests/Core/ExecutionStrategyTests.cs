using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class ExecutionStrategyTests : TestBase
    {
        [TestMethod]
        public void RunAll_ExecutionStrategyTests()
        {
            var result = RunTestGroup("ExecutionStrategy", builder =>
            {
                builder.Add("SequentialStrategy_ShouldExecuteInOrder", () =>
                {
                    var strategy = new SequentialExecutionStrategy();
                    var order = new List<string>();

                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", () => order.Add("stage1")),
                        new TestStage("stage2", () => order.Add("stage2")),
                        new TestStage("stage3", () => order.Add("stage3"))
                    };

                    strategy.Execute(stages, "input", new ErrorCollector(), new PipelineContext());

                    CollectionAssert.AreEqual(
                        new[] { "stage1", "stage2", "stage3" }, order);
                });

                builder.Add("SequentialStrategy_WithFatalError_ShouldStop", () =>
                {
                    var strategy = new SequentialExecutionStrategy();
                    var order = new List<string>();

                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", () => order.Add("stage1")),
                        new TestStage("stage2", () =>
                        {
                            order.Add("stage2");
                            throw new InvalidOperationException("fatal");
                        }) { IsFatal = true },
                        new TestStage("stage3", () => order.Add("stage3"))
                    };

                    var errors = new ErrorCollector();
                    strategy.Execute(stages, "input", errors, new PipelineContext());

                    CollectionAssert.AreEqual(new[] { "stage1", "stage2" }, order);
                    Assert.IsTrue(errors.HasErrors);
                });

                builder.Add("SequentialStrategy_WithNonFatalError_ShouldContinue", () =>
                {
                    var strategy = new SequentialExecutionStrategy();
                    var order = new List<string>();

                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", () => order.Add("stage1")),
                        new TestStage("stage2", () =>
                        {
                            order.Add("stage2");
                            throw new InvalidOperationException("non-fatal");
                        }) { IsFatal = false },
                        new TestStage("stage3", () => order.Add("stage3"))
                    };

                    var errors = new ErrorCollector();
                    strategy.Execute(stages, "input", errors, new PipelineContext());

                    CollectionAssert.AreEqual(new[] { "stage1", "stage2", "stage3" }, order);
                    Assert.IsTrue(errors.HasErrors);
                });

                builder.Add("ParallelStrategy_ShouldExecuteAllStages", () =>
                {
                    var strategy = new ParallelExecutionStrategy();
                    var executed = new List<string>();
                    var lockObj = new object();

                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", () =>
                        {
                            lock (lockObj) executed.Add("stage1");
                        }),
                        new TestStage("stage2", () =>
                        {
                            lock (lockObj) executed.Add("stage2");
                        }),
                        new TestStage("stage3", () =>
                        {
                            lock (lockObj) executed.Add("stage3");
                        })
                    };

                    strategy.Execute(stages, "input", new ErrorCollector(), new PipelineContext());

                    Assert.AreEqual(3, executed.Count);
                    Assert.IsTrue(executed.Contains("stage1"));
                    Assert.IsTrue(executed.Contains("stage2"));
                    Assert.IsTrue(executed.Contains("stage3"));
                });

                builder.Add("ParallelStrategy_WithMaxDegree_ShouldLimitConcurrency", () =>
                {
                    var strategy = new ParallelExecutionStrategy(maxDegreeOfParallelism: 2);
                    var executing = 0;
                    var maxConcurrent = 0;
                    var lockObj = new object();

                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", () =>
                        {
                            lock (lockObj)
                            {
                                executing++;
                                maxConcurrent = Math.Max(maxConcurrent, executing);
                            }
                            Thread.Sleep(100);
                            lock (lockObj) executing--;
                        }),
                        new TestStage("stage2", () =>
                        {
                            lock (lockObj)
                            {
                                executing++;
                                maxConcurrent = Math.Max(maxConcurrent, executing);
                            }
                            Thread.Sleep(100);
                            lock (lockObj) executing--;
                        }),
                        new TestStage("stage3", () =>
                        {
                            lock (lockObj)
                            {
                                executing++;
                                maxConcurrent = Math.Max(maxConcurrent, executing);
                            }
                            Thread.Sleep(100);
                            lock (lockObj) executing--;
                        })
                    };

                    strategy.Execute(stages, "input", new ErrorCollector(), new PipelineContext());

                    Assert.IsTrue(maxConcurrent <= 2,
                        $"Max concurrent was {maxConcurrent}, expected <= 2");
                });

                builder.Add("ParallelStrategy_WithFatalError_ShouldCancelOthers", () =>
                {
                    var strategy = new ParallelExecutionStrategy();
                    var executed = new List<string>();
                    var lockObj = new object();

                    var stages = new IPipelineStage[]
                    {
                        new TestStage("stage1", () =>
                        {
                            lock (lockObj) executed.Add("stage1");
                        }),
                        new TestStage("stage2", () =>
                        {
                            lock (lockObj) executed.Add("stage2");
                            throw new InvalidOperationException("fatal");
                        }) { IsFatal = true },
                        new TestStage("stage3", () =>
                        {
                            lock (lockObj) executed.Add("stage3");
                        })
                    };

                    var errors = new ErrorCollector();
                    strategy.Execute(stages, "input", errors, new PipelineContext());

                    Assert.IsTrue(errors.HasErrors);
                });

                builder.Add("DefaultStrategy_ShouldBeSequential", () =>
                {
                    var strategy = ExecutionStrategy.Default;
                    Assert.IsInstanceOfType(strategy, typeof(SequentialExecutionStrategy));
                });
            });
            result.AssertAllPassed();
        }

        private class TestStage : IPipelineStage
        {
            private readonly Action _action;

            public string Name { get; }
            public int Order { get; set; }
            public bool IsFatal { get; set; }

            public TestStage(string name, Action action)
            {
                Name = name;
                _action = action;
            }

            public object? Process(object input, ErrorCollector errors, PipelineContext context)
            {
                _action();
                return input;
            }
        }
    }
}
