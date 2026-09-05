using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class GraphRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_BasicOperations()
    {
        var result = new TestGroupBuilder("GraphRegistry_Basic")
            .Add("RegisterNode_ShouldAddNode", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "NodeA");
                return registry.GetNode("A") == "NodeA";
            })
            .Add("RegisterEdge_ShouldAddEdge", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "NodeA");
                registry.RegisterNode("B", "NodeB");
                registry.RegisterEdge("A", "B", "connects");
                var edges = registry.GetOutgoingEdges("A").ToList();
                return edges.Count == 1 && edges[0] == "connects";
            })
            .Add("GetOutgoingEdges_ShouldReturnConnectedEdges", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "NodeA");
                registry.RegisterNode("B", "NodeB");
                registry.RegisterNode("C", "NodeC");
                registry.RegisterEdge("A", "B", "edge1");
                registry.RegisterEdge("A", "C", "edge2");
                var edges = registry.GetOutgoingEdges("A").ToList();
                return edges.Count == 2
                    && edges.Contains("edge1")
                    && edges.Contains("edge2");
            })
            .Add("GetIncomingEdges_ShouldReturnIncomingEdges", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "A");
                registry.RegisterNode("B", "B");
                registry.RegisterEdge("A", "B", "edge");
                var incoming = registry.GetIncomingEdges("B").ToList();
                return incoming.Count == 1 && incoming[0] == "edge";
            })
            .Add("UnregisterNode_ShouldRemoveNodeAndEdges", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "A");
                registry.RegisterNode("B", "B");
                registry.RegisterEdge("A", "B", "edge");
                registry.UnregisterNode("A");
                return registry.GetNode("A") == null
                    && registry.GetOutgoingEdges("A").Count() == 0;
            })
            .Add("UnregisterEdge_ShouldRemoveEdgeOnly", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "A");
                registry.RegisterNode("B", "B");
                registry.RegisterEdge("A", "B", "edge");
                registry.UnregisterEdge("A", "B");
                return registry.GetNode("A") == "A"
                    && registry.GetNode("B") == "B"
                    && registry.GetOutgoingEdges("A").Count() == 0;
            })
            .Add("Clear_ShouldRemoveAll", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "A");
                registry.RegisterNode("B", "B");
                registry.RegisterEdge("A", "B", "edge");
                registry.Clear();
                return registry.GetAllNodes().Count() == 0
                    && registry.GetAllEdges().Count() == 0;
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_EdgeData()
    {
        var result = new TestGroupBuilder("GraphRegistry_EdgeData")
            .Add("EdgeWithWeight_ShouldStoreWeight", () =>
            {
                var registry = new GraphRegistryBase<string, string, double>();
                registry.RegisterNode("A", "NodeA");
                registry.RegisterNode("B", "NodeB");
                registry.RegisterEdge("A", "B", 3.14);
                var edges = registry.GetOutgoingEdges("A").ToList();
                return edges.Count == 1 && Math.Abs(edges[0] - 3.14) < 0.001;
            })
            .Add("MultipleEdges_FromSameNode", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("Center", "C");
                registry.RegisterNode("L1", "L1");
                registry.RegisterNode("L2", "L2");
                registry.RegisterNode("L3", "L3");
                registry.RegisterEdge("Center", "L1", "e1");
                registry.RegisterEdge("Center", "L2", "e2");
                registry.RegisterEdge("Center", "L3", "e3");
                return registry.GetOutgoingEdges("Center").Count() == 3;
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_TopologicalSort()
    {
        var result = new TestGroupBuilder("GraphRegistry_TopologicalSort")
            .Add("SimpleDAG_ShouldSortCorrectly", () =>
            {
                var registry = new GraphRegistryBase<string, string, string>();
                registry.RegisterNode("A", "A");
                registry.RegisterNode("B", "B");
                registry.RegisterNode("C", "C");
                registry.RegisterEdge("A", "B", "e1");
                registry.RegisterEdge("B", "C", "e2");
                var sorted = registry.TopologicalSort("A").ToList();
                return sorted.Count == 3
                    && sorted[0] == "C"
                    && sorted[1] == "B"
                    && sorted[2] == "A";
            })
            .Run();

        result.AssertAllPassed();
    }
}
