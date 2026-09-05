using MetaCommand;
using MetaCommand.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Validation
{
    [TestClass]
    public class GraphValidatorTests
    {
        [TestMethod]
        public void AddNode_NodeCountIncreases()
        {
            var validator = new GraphValidator<string>();
            validator.AddNode("A");
            validator.AddNode("B");
            Assert.AreEqual(2, validator.NodeCount);
        }

        [TestMethod]
        public void AddEdge_EdgeCountIncreases()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            Assert.AreEqual(1, validator.EdgeCount);
        }

        [TestMethod]
        public void AddEdges_BatchAddsEdges()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdges(new[] { ("A", "B"), ("B", "C") });
            Assert.AreEqual(2, validator.EdgeCount);
            Assert.AreEqual(3, validator.NodeCount);
        }

        [TestMethod]
        public void HasCycle_NoCycle_ReturnsFalse()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "C");
            Assert.IsFalse(validator.HasCycle());
        }

        [TestMethod]
        public void HasCycle_SimpleCycle_ReturnsTrue()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "A");
            Assert.IsTrue(validator.HasCycle());
        }

        [TestMethod]
        public void HasCycle_ThreeNodeCycle_ReturnsTrue()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "C");
            validator.AddEdge("C", "A");
            Assert.IsTrue(validator.HasCycle());
        }

        [TestMethod]
        public void FindCycles_WithCycle_ReturnsCyclePath()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "A");
            var cycles = validator.FindCycles();
            Assert.IsTrue(cycles.Count > 0);
        }

        [TestMethod]
        public void FindCycles_NoCycle_ReturnsEmpty()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            var cycles = validator.FindCycles();
            Assert.AreEqual(0, cycles.Count);
        }

        [TestMethod]
        public void TopologicalSort_NoCycle_ReturnsOrder()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "C");
            var sorted = validator.TopologicalSort();
            Assert.IsNotNull(sorted);
            Assert.AreEqual(3, sorted.Count);
        }

        [TestMethod]
        public void TopologicalSort_WithCycle_ReturnsNull()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "A");
            Assert.IsNull(validator.TopologicalSort());
        }

        [TestMethod]
        public void KahnSort_NoCycle_ReturnsOrder()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "C");
            var sorted = validator.KahnSort();
            Assert.IsNotNull(sorted);
            Assert.AreEqual(3, sorted.Count);
        }

        [TestMethod]
        public void KahnSort_WithCycle_ReturnsNull()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "A");
            Assert.IsNull(validator.KahnSort());
        }

        [TestMethod]
        public void GetReachableNodes_ReturnsAllReachable()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("B", "C");
            var reachable = validator.GetReachableNodes("A");
            Assert.AreEqual(3, reachable.Count);
            Assert.IsTrue(reachable.Contains("A"));
            Assert.IsTrue(reachable.Contains("B"));
            Assert.IsTrue(reachable.Contains("C"));
        }

        [TestMethod]
        public void GetInDegree_ReturnsCorrectCount()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("C", "B");
            Assert.AreEqual(2, validator.GetInDegree("B"));
            Assert.AreEqual(0, validator.GetInDegree("A"));
        }

        [TestMethod]
        public void GetOutDegree_ReturnsCorrectCount()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("A", "C");
            Assert.AreEqual(2, validator.GetOutDegree("A"));
            Assert.AreEqual(0, validator.GetOutDegree("B"));
        }

        [TestMethod]
        public void Clear_RemovesAllNodesAndEdges()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.Clear();
            Assert.AreEqual(0, validator.NodeCount);
            Assert.AreEqual(0, validator.EdgeCount);
        }

        [TestMethod]
        public void Nodes_ReturnsAllNodes()
        {
            var validator = new GraphValidator<string>();
            validator.AddEdge("A", "B");
            validator.AddEdge("C", "D");
            var nodes = validator.Nodes.ToList();
            Assert.AreEqual(4, nodes.Count);
        }
    }
}
