using MetaCommand;
using MetaCommand.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Validation
{
    [TestClass]
    public class CircularDependencyDetectorTests
    {
        [TestMethod]
        public void AddDependency_AddsEdge()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            var result = detector.Check();
            Assert.AreEqual(2, result.TotalNodes);
            Assert.AreEqual(1, result.TotalDependencies);
        }

        [TestMethod]
        public void AddDependencies_BatchAddsEdges()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependencies(new[] { ("A", "B"), ("B", "C") });
            var result = detector.Check();
            Assert.AreEqual(3, result.TotalNodes);
            Assert.AreEqual(2, result.TotalDependencies);
        }

        [TestMethod]
        public void HasCycle_NoCycle_ReturnsFalse()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.AddDependency("B", "C");
            Assert.IsFalse(detector.HasCycle());
        }

        [TestMethod]
        public void HasCycle_SimpleCycle_ReturnsTrue()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.AddDependency("B", "A");
            Assert.IsTrue(detector.HasCycle());
        }

        [TestMethod]
        public void HasCycle_ThreeNodeCycle_ReturnsTrue()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.AddDependency("B", "C");
            detector.AddDependency("C", "A");
            Assert.IsTrue(detector.HasCycle());
        }

        [TestMethod]
        public void HasCycle_SelfLoop_ReturnsTrue()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "A");
            Assert.IsTrue(detector.HasCycle());
        }

        [TestMethod]
        public void HasCycle_EmptyGraph_ReturnsFalse()
        {
            var detector = new CircularDependencyDetector<string>();
            Assert.IsFalse(detector.HasCycle());
        }

        [TestMethod]
        public void Check_NoCycle_ReturnsTopologicalOrder()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.AddDependency("B", "C");
            var result = detector.Check();
            Assert.IsFalse(result.HasCycle);
            Assert.IsNotNull(result.TopologicalOrder);
            Assert.AreEqual(3, result.TopologicalOrder.Count);
        }

        [TestMethod]
        public void Check_WithCycle_ReturnsCycles()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.AddDependency("B", "A");
            var result = detector.Check();
            Assert.IsTrue(result.HasCycle);
            Assert.IsTrue(result.Cycles.Count > 0);
        }

        [TestMethod]
        public void Check_EmptyGraph_ReturnsEmptyResult()
        {
            var detector = new CircularDependencyDetector<string>();
            var result = detector.Check();
            Assert.IsFalse(result.HasCycle);
            Assert.AreEqual(0, result.TotalNodes);
            Assert.AreEqual(0, result.TotalDependencies);
        }

        [TestMethod]
        public void Clear_RemovesAllNodesAndEdges()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.Clear();
            var result = detector.Check();
            Assert.AreEqual(0, result.TotalNodes);
            Assert.AreEqual(0, result.TotalDependencies);
        }

        [TestMethod]
        public void GetReport_WithCycle_ReturnsFormattedReport()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            detector.AddDependency("B", "A");
            var result = detector.Check();
            var report = result.GetReport();
            Assert.IsTrue(report.Contains("存在循环: True"));
            Assert.IsTrue(report.Contains("A → B"));
        }

        [TestMethod]
        public void GetReport_NoCycle_ReturnsFormattedReport()
        {
            var detector = new CircularDependencyDetector<string>();
            detector.AddDependency("A", "B");
            var result = detector.Check();
            var report = result.GetReport();
            Assert.IsTrue(report.Contains("存在循环: False"));
            Assert.IsTrue(report.Contains("拓扑排序"));
        }
    }
}
