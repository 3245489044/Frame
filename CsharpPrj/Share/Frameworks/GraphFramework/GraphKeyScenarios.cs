
#nullable enable

using GraphFoundation.Abstractions;

namespace GraphFramework
{
    public static class GraphKeyScenarios
    {
        public static void DemonstrateStringKeyGraph()
        {
            var graph = new GraphFoundation.Core.CoreGraph<string, object, object>();

            graph.AddNode("node1", new { Name = "Node 1" });
            graph.AddNode("node2", new { Name = "Node 2" });
            graph.AddEdge("edge1", "node1", "node2", true, new { Weight = 1.0 });

            Console.WriteLine($"String-keyed graph: {graph.GetAllNodeKeys().Count} nodes, {graph.GetAllEdgeIds().Count} edges");
        }

        public static void DemonstrateIntKeyGraph()
        {
            var graph = new GraphFoundation.Core.CoreGraph<int, object, object>();

            graph.AddNode(1, new { Name = "Node 1" });
            graph.AddNode(2, new { Name = "Node 2" });
            graph.AddEdge("edge1", 1, 2, true, new { Weight = 1.0 });

            Console.WriteLine($"Int-keyed graph: {graph.GetAllNodeKeys().Count} nodes, {graph.GetAllEdgeIds().Count} edges");
        }

        public static void DemonstrateGuidKeyGraph()
        {
            var graph = new GraphFoundation.Core.CoreGraph<Guid, object, object>();

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            graph.AddNode(id1, new { Name = "Node 1" });
            graph.AddNode(id2, new { Name = "Node 2" });
            graph.AddEdge("edge1", id1, id2, true, new { Weight = 1.0 });

            Console.WriteLine($"Guid-keyed graph: {graph.GetAllNodeKeys().Count} nodes, {graph.GetAllEdgeIds().Count} edges");
        }

        public static void DemonstrateCompositeKeyGraph()
        {
            var graph = new GraphFoundation.Core.CoreGraph<CompositeKey, object, object>();

            var key1 = new CompositeKey("GroupA", "Node1");
            var key2 = new CompositeKey("GroupA", "Node2");

            graph.AddNode(key1, new { Name = "Node 1" });
            graph.AddNode(key2, new { Name = "Node 2" });
            graph.AddEdge("edge1", key1, key2, true, new { Weight = 1.0 });

            Console.WriteLine($"Composite-keyed graph: {graph.GetAllNodeKeys().Count} nodes, {graph.GetAllEdgeIds().Count} edges");
        }

        public static void DemonstrateAll()
        {
            Console.WriteLine("=== Graph Key Scenarios ===\n");

            DemonstrateStringKeyGraph();
            DemonstrateIntKeyGraph();
            DemonstrateGuidKeyGraph();
            DemonstrateCompositeKeyGraph();

            Console.WriteLine("\n=== End of Graph Key Scenarios ===");
        }
    }

    public readonly struct CompositeKey : IEquatable<CompositeKey>
    {
        public string Group { get; }
        public string Id { get; }

        public CompositeKey(string group, string id)
        {
            Group = PortGuard.Required(group, nameof(group));
            Id = PortGuard.Required(id, nameof(id));
        }

        public bool Equals(CompositeKey other)
            => Group == other.Group && Id == other.Id;

        public override bool Equals(object? obj)
            => obj is CompositeKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Group, Id);

        public override string ToString()
            => $"{Group}:{Id}";

        public static bool operator ==(CompositeKey left, CompositeKey right)
            => left.Equals(right);

        public static bool operator !=(CompositeKey left, CompositeKey right)
            => !left.Equals(right);
    }
}
