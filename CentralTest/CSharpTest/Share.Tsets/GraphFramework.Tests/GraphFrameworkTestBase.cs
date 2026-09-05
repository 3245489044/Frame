
using ECS.Core;
using GraphFoundation.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    public abstract class GraphFrameworkTestBase : TestBase
    {
        protected static ICoreGraph<string, object, object> CreateStringGraph()
        {
            return GraphFrameworkUsage.CreateGraph<string, object, object>();
        }

        protected static ICoreGraph<int, object, object> CreateIntGraph()
        {
            return GraphFrameworkUsage.CreateGraph<int, object, object>();
        }

        protected static ICoreGraph<Guid, object, object> CreateGuidGraph()
        {
            return GraphFrameworkUsage.CreateGraph<Guid, object, object>();
        }

        protected static ICoreGraph<string, object, object> CreatePopulatedStringGraph()
        {
            var graph = CreateStringGraph();
            graph.AddNode("n1", new { Name = "Node 1", Value = 10 });
            graph.AddNode("n2", new { Name = "Node 2", Value = 20 });
            graph.AddNode("n3", new { Name = "Node 3", Value = 30 });
            graph.AddEdge("e1", "n1", "n2", true, new { Weight = 1.0 }, 1.0);
            graph.AddEdge("e2", "n2", "n3", true, new { Weight = 2.0 }, 2.0);
            return graph;
        }

        protected static ICoreGraph<string, object, object> CreateGraphWithDuplicateEdges()
        {
            var graph = CreateStringGraph();
            graph.AddNode("n1", new { Name = "Node 1" });
            graph.AddNode("n2", new { Name = "Node 2" });
            graph.AddEdge("e1", "n1", "n2", true, new { Weight = 1.0 }, 1.0);
            graph.AddEdge("e2", "n1", "n2", true, new { Weight = 2.0 }, 2.0);
            graph.AddEdge("e3", "n1", "n2", true, new { Weight = 3.0 }, 3.0);
            return graph;
        }

        protected static ICoreGraph<string, object, object> CreateGraphWithIsolatedNode()
        {
            var graph = CreateStringGraph();
            graph.AddNode("n1", new { Name = "Node 1" });
            graph.AddNode("n2", new { Name = "Node 2" });
            graph.AddNode("isolated", new { Name = "Isolated Node" });
            graph.AddEdge("e1", "n1", "n2", true, new { Weight = 1.0 }, 1.0);
            return graph;
        }

        protected static EcsWorld CreateEcsWorld()
        {
            return GraphFrameworkUsage.CreateWorld();
        }
    }
}
