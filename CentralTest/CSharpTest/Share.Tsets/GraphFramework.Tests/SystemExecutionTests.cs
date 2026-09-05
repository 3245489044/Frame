
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class SystemExecutionTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_Statistics()
        {
            var result = new TestGroupBuilder("Statistics")
                .Add("RunStatistics_ShouldReturnStats", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var stats = GraphFrameworkUsage.RunStatistics(graph);
                    return stats.NodeCount > 0;
                })
                .Add("RunStatistics_EmptyGraph_ShouldReturnZeroStats", () =>
                {
                    var graph = CreateStringGraph();
                    var stats = GraphFrameworkUsage.RunStatistics(graph);
                    return stats.NodeCount == 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Validation()
        {
            var result = new TestGroupBuilder("Validation")
                .Add("RunValidation_ValidGraph_ShouldPass", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var result2 = GraphFrameworkUsage.RunValidation(graph);
                    return result2.IsValid;
                })
                .Add("RunValidation_EmptyGraph_ShouldPass", () =>
                {
                    var graph = CreateStringGraph();
                    var result2 = GraphFrameworkUsage.RunValidation(graph);
                    return result2.IsValid;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Topology()
        {
            var result = new TestGroupBuilder("Topology")
                .Add("RunTopology_ShouldReturnResult", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var entityMap = GraphFrameworkUsage.CreateEntityMap<string>();
                    var topoResult = GraphFrameworkUsage.RunTopology(graph, entityMap);
                    return topoResult.SortedNodeKeys != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ChainProcessing()
        {
            var result = new TestGroupBuilder("ChainProcessing")
                .Add("RunChainProcessing_ShouldReturnResult", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var chainResult = GraphFrameworkUsage.RunChainProcessing(
                        graph,
                        keyFactory: s => s,
                        nodeDataFactory: key => new { Name = key.ToString() },
                        edgeDataFactory: (src, tgt) => new { Weight = 1.0 });
                    return chainResult.ChainCount > 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Transformation()
        {
            var result = new TestGroupBuilder("Transformation")
                .Add("RunTransformation_ShouldReturnResult", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var transformResult = GraphFrameworkUsage.RunTransformation(
                        graph,
                        nodeDataFactory: key => new { Name = key.ToString() },
                        edgeDataFactory: (src, tgt) => new { Weight = 1.0 },
                        operation: "reverse");
                    return transformResult.Success;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Orchestration()
        {
            var result = new TestGroupBuilder("Orchestration")
                .Add("RunOrchestration_ShouldReturnResult", () =>
                {
                    var graph = CreateStringGraph();
                    var template = new { Name = "Step", Position = 0 };
                    var orchResult = GraphFrameworkUsage.RunOrchestration(
                        graph, template, 2, 3,
                        keyGenerator: null,
                        keyExtractor: null,
                        setPosition: (data, pos) => { },
                        cloner: data => new { Name = ((dynamic)data!).Name, Position = ((dynamic)data!).Position });
                    return orchResult.ChainsCreated > 0;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
