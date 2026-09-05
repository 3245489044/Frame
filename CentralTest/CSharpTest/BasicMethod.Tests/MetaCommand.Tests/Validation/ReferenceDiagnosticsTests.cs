using MetaCommand;
using MetaCommand.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Validation
{
    [TestClass]
    public class ReferenceDiagnosticsTests
    {
        [TestMethod]
        public void AddReference_AddsEdge()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            var result = diagnostics.Diagnose();
            Assert.AreEqual(2, result.AllNodes.Count);
        }

        [TestMethod]
        public void AddReferences_BatchAddsEdges()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReferences(new[] { ("A", "B"), ("B", "C") });
            var result = diagnostics.Diagnose();
            Assert.AreEqual(3, result.AllNodes.Count);
        }

        [TestMethod]
        public void AddIsolatedNode_AddsNode()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddIsolatedNode("A");
            var result = diagnostics.Diagnose();
            Assert.AreEqual(1, result.AllNodes.Count);
            Assert.AreEqual(1, result.IsolatedNodes.Count);
        }

        [TestMethod]
        public void Diagnose_NoCycles_ReturnsCorrectStructure()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.AddReference("B", "C");
            var result = diagnostics.Diagnose();
            Assert.IsFalse(result.HasCycles);
            Assert.AreEqual(3, result.AllNodes.Count);
            Assert.AreEqual(1, result.RootNodes.Count);
            Assert.AreEqual("A", result.RootNodes[0]);
            Assert.AreEqual(1, result.LeafNodes.Count);
            Assert.AreEqual("C", result.LeafNodes[0]);
        }

        [TestMethod]
        public void Diagnose_WithCycle_DetectsCycle()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.AddReference("B", "A");
            var result = diagnostics.Diagnose();
            Assert.IsTrue(result.HasCycles);
            Assert.IsTrue(result.Cycles.Count > 0);
        }

        [TestMethod]
        public void Diagnose_WithIsolatedNode_IdentifiesIsolated()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.AddIsolatedNode("C");
            var result = diagnostics.Diagnose();
            Assert.AreEqual(3, result.AllNodes.Count);
            Assert.AreEqual(1, result.IsolatedNodes.Count);
            Assert.AreEqual("C", result.IsolatedNodes[0]);
        }

        [TestMethod]
        public void Diagnose_Empty_ReturnsEmptyResult()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            var result = diagnostics.Diagnose();
            Assert.AreEqual(0, result.AllNodes.Count);
            Assert.IsFalse(result.HasCycles);
        }

        [TestMethod]
        public void GetReferenceChains_ReturnsChains()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.AddReference("B", "C");
            var chains = diagnostics.GetReferenceChains("A");
            Assert.IsTrue(chains.Count > 0);
            Assert.IsTrue(chains[0].Contains("A"));
            Assert.IsTrue(chains[0].Contains("B"));
            Assert.IsTrue(chains[0].Contains("C"));
        }

        [TestMethod]
        public void GetReferenceChains_WithBranch_ReturnsAllBranches()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.AddReference("A", "C");
            var chains = diagnostics.GetReferenceChains("A");
            Assert.AreEqual(2, chains.Count);
        }

        [TestMethod]
        public void Clear_RemovesAllNodesAndEdges()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.Clear();
            var result = diagnostics.Diagnose();
            Assert.AreEqual(0, result.AllNodes.Count);
        }

        [TestMethod]
        public void GetReport_WithCycle_ReturnsFormattedReport()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            diagnostics.AddReference("B", "A");
            var result = diagnostics.Diagnose();
            var report = result.GetReport();
            Assert.IsTrue(report.Contains("存在循环: True"));
        }

        [TestMethod]
        public void GetReport_NoCycle_ReturnsFormattedReport()
        {
            var diagnostics = new ReferenceDiagnostics<string>();
            diagnostics.AddReference("A", "B");
            var result = diagnostics.Diagnose();
            var report = result.GetReport();
            Assert.IsTrue(report.Contains("存在循环: False"));
            Assert.IsTrue(report.Contains("根节点"));
            Assert.IsTrue(report.Contains("叶节点"));
        }
    }
}
