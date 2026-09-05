
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class ChainOrchestrationTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_CreateChainFromTemplate()
        {
            var result = new TestGroupBuilder("CreateChainFromTemplate")
                .Add("CreateChainFromTemplate_ShouldCreateNodes", () =>
                {
                    var graph = CreateStringGraph();
                    var template = new { Name = "Step", Position = 0 };
                    var chainResult = graph.CreateChainFromTemplate(template, 3,
                        keyGenerator: null,
                        keyExtractor: null,
                        setPosition: (data, pos) => { },
                        cloner: data => new { Name = ((dynamic)data!).Name, Position = ((dynamic)data!).Position });
                    return chainResult.NodeKeys.Count == 3;
                })
                .Add("CreateChainFromTemplate_WithCount1_ShouldCreate1Node", () =>
                {
                    var graph = CreateStringGraph();
                    var template = new { Name = "Step", Position = 0 };
                    var chainResult = graph.CreateChainFromTemplate(template, 1,
                        keyGenerator: null,
                        keyExtractor: null,
                        setPosition: (data, pos) => { },
                        cloner: data => new { Name = ((dynamic)data!).Name, Position = ((dynamic)data!).Position });
                    return chainResult.NodeKeys.Count == 1;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_CreateMultipleChains()
        {
            var result = new TestGroupBuilder("CreateMultipleChains")
                .Add("CreateMultipleChains_ShouldCreateCorrectCount", () =>
                {
                    var graph = CreateStringGraph();
                    var template = new { Name = "Step", Position = 0 };
                    var orchResult = graph.CreateMultipleChains(template, 2, 3,
                        keyGenerator: null,
                        keyExtractor: null,
                        setPosition: (data, pos) => { },
                        cloner: data => new { Name = ((dynamic)data!).Name, Position = ((dynamic)data!).Position });
                    return orchResult.Chains.Count == 2;
                })
                .Add("CreateMultipleChains_EachChainShouldHaveCorrectNodes", () =>
                {
                    var graph = CreateStringGraph();
                    var template = new { Name = "Step", Position = 0 };
                    var orchResult = graph.CreateMultipleChains(template, 2, 3,
                        keyGenerator: null,
                        keyExtractor: null,
                        setPosition: (data, pos) => { },
                        cloner: data => new { Name = ((dynamic)data!).Name, Position = ((dynamic)data!).Position });
                    return orchResult.Chains.All(c => c.NodeKeys.Count == 3);
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ConnectChains()
        {
            var result = new TestGroupBuilder("ConnectChains")
                .Add("ConnectChains_TwoChains_ShouldMerge", () =>
                {
                    var graph = CreateStringGraph();
                    var template = new { Name = "Step", Position = 0 };
                    var orchResult = graph.CreateMultipleChains(template, 2, 2,
                        keyGenerator: null,
                        keyExtractor: null,
                        setPosition: (data, pos) => { },
                        cloner: data => new { Name = ((dynamic)data!).Name, Position = ((dynamic)data!).Position });
                    var merged = graph.ConnectChains(orchResult.Chains);
                    return merged.NodeKeys.Count == 4;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_RebuildSequentialRelations()
        {
            var result = new TestGroupBuilder("RebuildSequentialRelations")
                .Add("RebuildSequentialRelations_ShouldCreateEdges", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("a", new { Name = "A" });
                    graph.AddNode("b", new { Name = "B" });
                    graph.AddNode("c", new { Name = "C" });
                    graph.RebuildSequentialRelations(new[] { "a", "b", "c" }, "chain1");
                    return graph.GetAllEdgeIds().Count == 2;
                })
                .Add("RebuildSequentialRelations_SingleNode_ShouldNotCreateEdges", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("a", new { Name = "A" });
                    graph.RebuildSequentialRelations(new[] { "a" }, "chain1");
                    return graph.GetAllEdgeIds().Count == 0;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
