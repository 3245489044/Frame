using GraphFoundation.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.GraphProcessing.Snapshots;

namespace GraphProcessing.Tests
{
    [TestClass]
    public class SnapshotsTests
    {
        [TestMethod]
        public void SnapshotDiff_DetectsAddAndRemove()
        {
            var graph = new CoreGraph<string, object, object>();
            graph.AddNode("A", new object());
            graph.AddNode("B", new object());
            graph.AddEdge("e1", "A", "B", isDirected: true, data: null, weight: 1.0);

            var before = GraphSnapshot<object, object>.CreateFrom(graph, "g");
            Assert.AreEqual(2, before.NodeCount);
            Assert.AreEqual(1, before.EdgeCount);

            graph.AddNode("C", new object());
            graph.RemoveNode("A");

            var after = GraphSnapshot<object, object>.CreateFrom(graph, "g", version: 2);
            var diff = GraphDiff<object, object>.Calculate(before, after);

            Assert.IsTrue(diff.HasChanges);
            CollectionAssert.Contains(diff.AddedNodeKeys.ToList(), "C");
            CollectionAssert.Contains(diff.RemovedNodeKeys.ToList(), "A");
        }

        [TestMethod]
        public void Snapshot_IdenticalHasNoChanges()
        {
            var graph = new CoreGraph<string, object, object>();
            graph.AddNode("A", new object());
            graph.AddNode("B", new object());

            var s1 = GraphSnapshot<object, object>.CreateFrom(graph, "g");
            var s2 = GraphSnapshot<object, object>.CreateFrom(graph, "g", version: 2);
            var diff = GraphDiff<object, object>.Calculate(s1, s2);

            Assert.IsFalse(diff.HasChanges);
        }
    }
}
