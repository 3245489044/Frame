using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.GraphProcessing.Algorithms;
using Modules.GraphProcessing.Diagnostics;

namespace GraphProcessing.Tests
{
    [TestClass]
    public class AlgorithmsTests
    {
        private static Dictionary<string, List<string>> SimpleDag()
        {
            return new Dictionary<string, List<string>>
            {
                ["A"] = new() { "B", "C" },
                ["B"] = new() { "D" },
                ["C"] = new() { "D" },
                ["D"] = new()
            };
        }

        private static Dictionary<string, List<string>> CyclicGraph()
        {
            return new Dictionary<string, List<string>>
            {
                ["A"] = new() { "B" },
                ["B"] = new() { "C" },
                ["C"] = new() { "A" }
            };
        }

        [TestMethod]
        public void CycleDetector_DetectsSingleCycle()
        {
            var g = CyclicGraph();
            var detector = new DefaultCycleDetector<string>();
            var cycles = detector.FindCycles(g.Keys.ToList(), k => g.TryGetValue(k, out var v) ? v : new List<string>());

            Assert.AreEqual(1, cycles.Count);
            CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, cycles[0].ToArray());
            Assert.IsTrue(detector.HasCycle(g.Keys.ToList(), k => g.TryGetValue(k, out var v) ? v : new List<string>()));
        }

        [TestMethod]
        public void CycleDetector_NoCycleOnDag()
        {
            var g = SimpleDag();
            var detector = new DefaultCycleDetector<string>();
            var cycles = detector.FindCycles(g.Keys.ToList(), k => g.TryGetValue(k, out var v) ? v : new List<string>());
            Assert.AreEqual(0, cycles.Count);
        }

        [TestMethod]
        public void ShortestPath_FindsBfsPath()
        {
            var g = SimpleDag();
            var finder = new DefaultShortestPathFinder<string>();
            var path = finder.FindShortestPath("A", "D", k => g.TryGetValue(k, out var v) ? v : new List<string>());

            Assert.AreEqual("A", path[0]);
            Assert.AreEqual("D", path[^1]);
            Assert.AreEqual(3, path.Count); // A -> (B|C) -> D
        }

        [TestMethod]
        public void ShortestPath_UnreachableReturnsEmpty()
        {
            var g = SimpleDag();
            var finder = new DefaultShortestPathFinder<string>();
            var path = finder.FindShortestPath("D", "A", k => g.TryGetValue(k, out var v) ? v : new List<string>());
            Assert.AreEqual(0, path.Count);
        }

        [TestMethod]
        public void PathFinder_FindAllPaths_BacktracksVisited()
        {
            // 菱形图：A->B, A->C, B->D, C->D，两条路径 A-B-D 与 A-C-D。
            var g = SimpleDag();
            var finder = new DefaultPathFinder<string>();
            var paths = finder.FindAllPaths("A", "D", k => g.TryGetValue(k, out var v) ? v : new List<string>());

            Assert.AreEqual(2, paths.Count, "visited 回溯缺陷会导致只找到一条路径");
        }

        [TestMethod]
        public void PathFinder_ReachableNodes()
        {
            var g = SimpleDag();
            var finder = new DefaultPathFinder<string>();
            var reachable = finder.GetReachableNodes("A", k => g.TryGetValue(k, out var v) ? v : new List<string>());
            CollectionAssert.AreEquivalent(new[] { "A", "B", "C", "D" }, reachable.ToArray());
        }

        [TestMethod]
        public void ReferenceDiagnostics_DetectsOrphanAndMissing()
        {
            var references = new Dictionary<string, IEnumerable<string>>
            {
                ["X"] = new List<string> { "Y", "Z" },
                ["Y"] = new List<string>()
            };
            var diag = new DefaultReferenceDiagnostics();
            var result = diag.RunFullDiagnostics(
                references,
                k => references.TryGetValue(k, out var v) ? v : new List<string>(),
                () => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X" });

            Assert.IsTrue(result.HasIssues);
            Assert.AreEqual(1, result.OrphanedCount);       // Y 未被引用（作为节点引用来源）
            Assert.AreEqual(1, result.MissingCount);         // Z 不存在
        }
    }
}
