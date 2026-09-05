
using GraphFoundation.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class GraphBuildingTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_CreateGraph()
        {
            var result = new TestGroupBuilder("CreateGraph")
                .Add("CreateStringKeyGraph_ShouldSucceed", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("n1", new { Name = "Node1" });
                    return graph.GetAllNodeKeys().Count == 1;
                })
                .Add("CreateIntKeyGraph_ShouldSucceed", () =>
                {
                    var graph = CreateIntGraph();
                    graph.AddNode(1, new { Name = "Node1" });
                    return graph.ContainsNode(1);
                })
                .Add("CreateGuidKeyGraph_ShouldSucceed", () =>
                {
                    var graph = CreateGuidGraph();
                    var id = Guid.NewGuid();
                    graph.AddNode(id, new { Name = "Node1" });
                    return graph.ContainsNode(id);
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_NodeOperations()
        {
            var result = new TestGroupBuilder("NodeOperations")
                .Add("AddNode_ShouldIncreaseCount", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("n1", new { Name = "Test" });
                    graph.AddNode("n2", new { Name = "Test2" });
                    return graph.GetAllNodeKeys().Count == 2;
                })
                .Add("RemoveNode_ShouldDecreaseCount", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    graph.RemoveNode("n1");
                    return graph.GetAllNodeKeys().Count == 2 && !graph.ContainsNode("n1");
                })
                .Add("ContainsNode_NonExistent_ShouldReturnFalse", () =>
                {
                    var graph = CreateStringGraph();
                    return !graph.ContainsNode("nonexistent");
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EdgeOperations()
        {
            var result = new TestGroupBuilder("EdgeOperations")
                .Add("AddEdge_ShouldCreateConnection", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("n1", new { Name = "Source" });
                    graph.AddNode("n2", new { Name = "Target" });
                    graph.AddEdge("e1", "n1", "n2", true, new { Weight = 1.0 }, 1.0);
                    return graph.ContainsEdge("e1");
                })
                .Add("RemoveEdge_ShouldRemoveConnection", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    graph.RemoveEdge("e1");
                    return !graph.ContainsEdge("e1");
                })
                .Add("GetEdgeWeight_ShouldReturnCorrectWeight", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var weight = graph.GetEdgeWeight("e1");
                    return Math.Abs(weight - 1.0) < 0.001;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_GraphProperties()
        {
            var result = new TestGroupBuilder("GraphProperties")
                .Add("EmptyGraph_ShouldHaveZeroNodes", () =>
                {
                    var graph = CreateStringGraph();
                    return graph.GetAllNodeKeys().Count == 0;
                })
                .Add("EmptyGraph_ShouldHaveZeroEdges", () =>
                {
                    var graph = CreateStringGraph();
                    return graph.GetAllEdgeIds().Count == 0;
                })
                .Add("PopulatedGraph_ShouldHaveCorrectNodeCount", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    return graph.GetAllNodeKeys().Count == 3;
                })
                .Add("PopulatedGraph_ShouldHaveCorrectEdgeCount", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    return graph.GetAllEdgeIds().Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
