
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class UtilityTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_DeepClone()
        {
            var result = new TestGroupBuilder("DeepClone")
                .Add("DeepClone_SimpleObject_ShouldCreateCopy", () =>
                {
                    var original = new { Name = "Test", Value = 42 };
                    var clone = GraphFrameworkUsage.DeepClone(original);
                    return clone != null;
                })
                .Add("DeepClone_List_ShouldCreateCopy", () =>
                {
                    var original = new List<string> { "a", "b", "c" };
                    var clone = GraphFrameworkUsage.DeepClone(original);
                    return clone != null && clone.Count == 3;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_TraverseGraph()
        {
            var result = new TestGroupBuilder("TraverseGraph")
                .Add("TraverseGraph_ShouldVisitAllNodes", () =>
                {
                    var obj = new { A = new { Value = 1 }, B = new { Value = 2 } };
                    var visited = new List<object>();
                    var result2 = obj.TraverseGraph(node =>
                    {
                        visited.Add(node);
                        return node;
                    });
                    return visited.Count > 0;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DefaultGenerators()
        {
            var result = new TestGroupBuilder("DefaultGenerators")
                .Add("CreateDefaultKeyGenerator_ShouldSucceed", () =>
                {
                    var generator = GraphFrameworkUsage.CreateDefaultKeyGenerator<string>();
                    return generator != null;
                })
                .Add("CreateDefaultNodeGenerator_ShouldSucceed", () =>
                {
                    var generator = GraphFrameworkUsage.CreateDefaultNodeGenerator<object>();
                    return generator != null;
                })
                .Add("CreateDefaultEdgeManager_ShouldSucceed", () =>
                {
                    var manager = GraphFrameworkUsage.CreateDefaultEdgeManager<string, object, object>();
                    return manager != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_NormalizationContext()
        {
            var result = new TestGroupBuilder("NormalizationContext")
                .Add("CreateNormalizationContext_ShouldSucceed", () =>
                {
                    var context = GraphFrameworkUsage.CreateNormalizationContext();
                    return context != null;
                })
                .Add("BeginNormalizationContextScope_ShouldSucceed", () =>
                {
                    using var scope = GraphFrameworkUsage.BeginNormalizationContextScope();
                    return scope != null;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
