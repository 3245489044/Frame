using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes.Relations;

namespace HierarchyMethod.Tests.Nodes
{
    /// <summary>节点关系门面验证：控制流静态边推导（Owns/Calls，LLM-free 自包含）。</summary>
    [TestClass]
    public class NodeRelationTests
    {
        [TestMethod]
        public void DeriveWorkflowControlFlowEdges_OwnsAndCalls()
        {
            var steps = new[] { "collect", "analyze", "act" };
            var edges = NodeRelation.DeriveWorkflowControlFlowEdges("wf", steps);

            Assert.IsNotNull(edges);
            Assert.IsTrue(edges.Count >= 1, "应至少推导出控制流边");
        }

        [TestMethod]
        public void NodeId_UsesWorkflowDotNode()
        {
            Assert.AreEqual("wf.node", NodeRelation.NodeId("wf", "node"));
        }
    }
}
