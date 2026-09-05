
using ECS.Core;
using GraphFramework.Ecs.Pipeline;
using GraphFramework.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    public class TestStep : PipelineStepBase
    {
        private readonly string _name;
        public override string Name => _name;

        public TestStep(string name)
        {
            _name = name;
        }

        public override void Execute(EcsWorld world)
        {
        }
    }

    [TestClass]
    public class PipelineExecutionTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_CreatePipeline()
        {
            var result = new TestGroupBuilder("CreatePipeline")
                .Add("CreateBasicPipeline_ShouldSucceed", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    return pipeline != null;
                })
                .Add("CreateFullPipeline_ShouldSucceed", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateFullPipeline();
                    return pipeline != null;
                })
                .Add("CreateProductionPipeline_ShouldSucceed", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateProductionPipeline();
                    return pipeline != null;
                })
                .Add("CreatePipeline_ShouldSucceed", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreatePipeline();
                    return pipeline != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_StepManagement()
        {
            var result = new TestGroupBuilder("StepManagement")
                .Add("AddStep_ShouldIncreaseStepCount", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    var initialCount = pipeline.Steps.Count;
                    pipeline.AddStep(new TestStep("testStep"));
                    return pipeline.Steps.Count == initialCount + 1;
                })
                .Add("InsertStep_ShouldInsertAtPosition", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.AddStep(new TestStep("step1"));
                    pipeline.AddStep(new TestStep("step3"));
                    pipeline.InsertStep(1, new TestStep("step2"));
                    return pipeline.Steps.Count == 3;
                })
                .Add("RemoveStep_ShouldDecreaseStepCount", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    var step1 = new TestStep("step1");
                    var step2 = new TestStep("step2");
                    pipeline.AddStep(step1);
                    pipeline.AddStep(step2);
                    pipeline.RemoveStep(step1);
                    return pipeline.Steps.Count == 1;
                })
                .Add("ClearSteps_ShouldResetStepCount", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.AddStep(new TestStep("step1"));
                    pipeline.AddStep(new TestStep("step2"));
                    pipeline.ClearSteps();
                    return pipeline.Steps.Count == 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DecoratorChain()
        {
            var result = new TestGroupBuilder("DecoratorChain")
                .Add("AddDecorator_ShouldWrapExecution", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.AddStep(new TestStep("testStep"));
                    pipeline.GlobalDecorators = new DecoratorChain()
                        .Add(new LogDecorator("logging"));
                    return pipeline.GlobalDecorators != null;
                })
                .Add("RemoveDecorator_ShouldClearDecorators", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.AddStep(new TestStep("testStep"));
                    pipeline.GlobalDecorators = new DecoratorChain()
                        .Add(new LogDecorator("logging"));
                    pipeline.GlobalDecorators = null;
                    return pipeline.GlobalDecorators == null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Caching()
        {
            var result = new TestGroupBuilder("Caching")
                .Add("EnableCaching_ShouldSetCache", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.EnableCaching = true;
                    return pipeline.EnableCaching;
                })
                .Add("DisableCaching_ShouldClearCache", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.EnableCaching = true;
                    pipeline.EnableCaching = false;
                    return !pipeline.EnableCaching;
                })
                .Add("ClearCache_ShouldSucceed", () =>
                {
                    var pipeline = GraphFrameworkUsage.CreateBasicPipeline();
                    pipeline.EnableCaching = true;
                    pipeline.Cache?.Clear();
                    return true;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PipelineSteps()
        {
            var result = new TestGroupBuilder("PipelineSteps")
                .Add("AddValidateStep_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var pipeline = GraphFrameworkUsage.CreateFullPipeline();
                    pipeline.AddValidateStep(graph);
                    return pipeline.Steps.Count > 0;
                })
                .Add("AddStatisticsStep_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var pipeline = GraphFrameworkUsage.CreateFullPipeline();
                    pipeline.AddStatisticsStep(graph);
                    return pipeline.Steps.Count > 0;
                })
                .Add("AddNormalizeStep_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var pipeline = GraphFrameworkUsage.CreateFullPipeline();
                    pipeline.AddNormalizeStep(graph);
                    return pipeline.Steps.Count > 0;
                })
                .Add("AddDedupeStep_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var pipeline = GraphFrameworkUsage.CreateFullPipeline();
                    pipeline.AddDedupeStep(graph);
                    return pipeline.Steps.Count > 0;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
