
using GraphFoundation.Abstractions;
using GraphFoundation.Transform;
using GraphFramework.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class GraphTransformationTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_ReverseEdges()
        {
            var result = new TestGroupBuilder("ReverseEdges")
                .Add("ReverseEdges_ShouldSwapSourceAndTarget", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    GraphTransformer.ReverseEdges(graph);
                    var source = graph.GetEdgeSource("e1");
                    var target = graph.GetEdgeTarget("e1");
                    return source.Equals("n2") && target.Equals("n1");
                })
                .Add("ReverseEdges_ShouldPreserveEdgeCount", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var count = GraphTransformer.ReverseEdges(graph);
                    return count == 2 && graph.GetAllEdgeIds().Count == 2;
                })
                .Add("ReverseEdges_EmptyGraph_ShouldReturnZero", () =>
                {
                    var graph = CreateStringGraph();
                    var count = GraphTransformer.ReverseEdges(graph);
                    return count == 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ExtractSubgraph()
        {
            var result = new TestGroupBuilder("ExtractSubgraph")
                .Add("ExtractSubgraph_WithExistingKeys_ShouldReturnKeys", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var keys = GraphTransformer.ExtractSubgraph(graph, new[] { "n1", "n2" });
                    return keys.Length == 2;
                })
                .Add("ExtractSubgraph_WithNonExistentKeys_ShouldReturnEmpty", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var keys = GraphTransformer.ExtractSubgraph(graph, new[] { "nonexistent" });
                    return keys.Length == 0;
                })
                .Add("ExtractSubgraph_WithEmptyKeys_ShouldReturnEmpty", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var keys = GraphTransformer.ExtractSubgraph(graph, Array.Empty<string>());
                    return keys.Length == 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_MergeNodes()
        {
            var result = new TestGroupBuilder("MergeNodes")
                .Add("MergeNodes_ShouldTransferEdges", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    GraphTransformer.MergeNodes(graph, "n1", "n2",
                        key => new { Name = $"Merged_{key}" },
                        (src, tgt) => new { Weight = 1.0 });
                    return graph.ContainsNode("n1") && !graph.ContainsNode("n2");
                })
                .Add("MergeNodes_NonExistentTarget_ShouldNotThrow", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    GraphTransformer.MergeNodes(graph, "nonexistent", "n2",
                        key => new { Name = "Test" },
                        (src, tgt) => new { Weight = 1.0 });
                    return graph.GetAllNodeKeys().Count == 3;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
