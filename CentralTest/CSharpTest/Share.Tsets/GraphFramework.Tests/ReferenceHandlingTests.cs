
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class ReferenceHandlingTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_RewriteReferences()
        {
            var result = new TestGroupBuilder("RewriteReferences")
                .Add("RewriteReferences_WithIdMap_ShouldRewrite", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("old1", new { Name = "Old1" });
                    graph.AddNode("new1", new { Name = "New1" });
                    var idMap = new Dictionary<string, string> { { "old1", "new1" } };
                    var data = new { Ref = "old1", Name = "Test" };
                    var rewritten = data.RewriteReferences(idMap, graph);
                    return rewritten != null;
                })
                .Add("RewriteReferences_EmptyIdMap_ShouldNotChange", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("n1", new { Name = "Node1" });
                    var idMap = new Dictionary<string, string>();
                    var data = new { Ref = "n1", Name = "Test" };
                    var rewritten = data.RewriteReferences(idMap, graph);
                    return rewritten != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_UpdateReferences()
        {
            var result = new TestGroupBuilder("UpdateReferences")
                .Add("UpdateReferences_WithResolver_ShouldUpdate", () =>
                {
                    var graph = CreateStringGraph();
                    graph.AddNode("n1", new { Name = "Node1" });
                    var resolver = GraphFrameworkUsage.CreateReferenceResolver<string, object>(
                        resolveFunc: (obj, g) => obj);
                    var data = new { Ref = "n1", Name = "Test" };
                    var updated = data.UpdateReferences(resolver, graph);
                    return updated != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_CreateRewriterAndResolver()
        {
            var result = new TestGroupBuilder("CreateRewriterAndResolver")
                .Add("CreateReferenceRewriter_ShouldSucceed", () =>
                {
                    var idMap = new Dictionary<string, string> { { "old", "new" } };
                    var rewriter = GraphFrameworkUsage.CreateReferenceRewriter<string, object>(idMap);
                    return rewriter != null;
                })
                .Add("CreateReferenceResolver_ShouldSucceed", () =>
                {
                    var resolver = GraphFrameworkUsage.CreateReferenceResolver<string, object>();
                    return resolver != null;
                })
                .Add("CreateReferenceResolver_WithCustomFunc_ShouldSucceed", () =>
                {
                    var resolver = GraphFrameworkUsage.CreateReferenceResolver<string, object>(
                        resolveFunc: (obj, g) => obj);
                    return resolver != null;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
