
using GraphFoundation.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class NormalizationTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_Normalize()
        {
            var result = new TestGroupBuilder("Normalize")
                .Add("Normalize_WithIsolatedNode_ShouldRemoveIt", () =>
                {
                    var graph = CreateGraphWithIsolatedNode();
                    var result2 = NormalizationExtensions.Normalize(graph);
                    return !graph.ContainsNode("isolated");
                })
                .Add("Normalize_WithDuplicateEdges_ShouldMerge", () =>
                {
                    var graph = CreateGraphWithDuplicateEdges();
                    var result2 = NormalizationExtensions.Normalize(graph);
                    return graph.GetAllEdgeIds().Count < 3;
                })
                .Add("Normalize_CleanGraph_ShouldNotChange", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var result2 = NormalizationExtensions.Normalize(graph);
                    return graph.GetAllNodeKeys().Count == 3 && graph.GetAllEdgeIds().Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DeduplicateByWeight()
        {
            var result = new TestGroupBuilder("DeduplicateByWeight")
                .Add("DeduplicateByWeight_ShouldKeepHighestWeight", () =>
                {
                    var graph = CreateGraphWithDuplicateEdges();
                    var result2 = NormalizationExtensions.DeduplicateByWeight(graph);
                    var remainingEdges = graph.GetAllEdgeIds().ToList();
                    return remainingEdges.Count == 1;
                })
                .Add("DeduplicateByWeight_NoDuplicates_ShouldNotChange", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var result2 = NormalizationExtensions.DeduplicateByWeight(graph);
                    return graph.GetAllEdgeIds().Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DeduplicateStrict()
        {
            var result = new TestGroupBuilder("DeduplicateStrict")
                .Add("DeduplicateStrict_WithDuplicateEdges_ShouldMerge", () =>
                {
                    var graph = CreateGraphWithDuplicateEdges();
                    var result2 = NormalizationExtensions.DeduplicateStrict(graph);
                    return graph.GetAllEdgeIds().Count == 1;
                })
                .Add("DeduplicateStrict_NoDuplicates_ShouldNotChange", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var result2 = NormalizationExtensions.DeduplicateStrict(graph);
                    return graph.GetAllEdgeIds().Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DeduplicateUndirected()
        {
            var result = new TestGroupBuilder("DeduplicateUndirected")
                .Add("DeduplicateUndirected_WithOppositeEdges_ShouldMerge", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("n1", new { Name = "Node 1" });
                    graph.AddNode("n2", new { Name = "Node 2" });
                    graph.AddEdge("e1", "n1", "n2", true, new { Weight = 1.0 }, 1.0);
                    graph.AddEdge("e2", "n2", "n1", true, new { Weight = 2.0 }, 2.0);
                    var result2 = NormalizationExtensions.DeduplicateUndirected(graph);
                    return graph.GetAllEdgeIds().Count == 1;
                })
                .Add("DeduplicateUndirected_NoOppositeEdges_ShouldNotChange", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var result2 = NormalizationExtensions.DeduplicateUndirected(graph);
                    return graph.GetAllEdgeIds().Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_SmartDeduplicate()
        {
            var result = new TestGroupBuilder("SmartDeduplicate")
                .Add("SmartDeduplicate_WithSimilarStrings_ShouldMerge", () =>
                {
                    var items = new[] { "hello_world", "hello-world", "hello.world", "different" };
                    var result2 = GraphFrameworkUsage.SmartDeduplicate(items, 0.7);
                    return result2.Matches.Count > 0;
                })
                .Add("SmartDeduplicate_WithThreshold1_ShouldNotMerge", () =>
                {
                    var items = new[] { "hello", "world" };
                    var result2 = GraphFrameworkUsage.SmartDeduplicate(items, 1.0);
                    return result2.Matches.Count == 0;
                })
                .Add("SmartDeduplicate_EmptyList_ShouldReturnEmpty", () =>
                {
                    var items = Array.Empty<string>();
                    var result2 = GraphFrameworkUsage.SmartDeduplicate(items);
                    return result2.Matches.Count == 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_IdentifierExtractor()
        {
            var result = new TestGroupBuilder("IdentifierExtractor")
                .Add("ExtractIdentifier_ShouldNormalizeCase", () =>
                {
                    var extracted = GraphFrameworkUsage.ExtractIdentifier("HelloWorld");
                    return extracted == "hello_world";
                })
                .Add("AreIdentifiersMatching_Similar_ShouldMatch", () =>
                {
                    return GraphFrameworkUsage.AreIdentifiersMatching("hello_world", "hello-world");
                })
                .Add("AreIdentifiersMatching_Different_ShouldNotMatch", () =>
                {
                    return !GraphFrameworkUsage.AreIdentifiersMatching("hello", "world");
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
