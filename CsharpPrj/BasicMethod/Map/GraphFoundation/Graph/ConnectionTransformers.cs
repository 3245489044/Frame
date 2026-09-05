
#nullable enable

namespace GraphFoundation.Topology.Transformers
{
    public class Connection
    {
        public string Id { get; }
        public string Source { get; }
        public string Target { get; }
        public bool IsDirected { get; }
        public double Weight { get; }

        public Connection(string id, string source, string target, bool isDirected, double weight = 1.0)
        {
            Id = id;
            Source = source;
            Target = target;
            IsDirected = isDirected;
            Weight = weight;
        }
    }

    public interface ITransformationContext
    {
        IReadOnlyList<Connection> Connections { get; }
        void AddConnection(Connection connection);
        void RemoveConnection(string id);
    }

    public interface IConnectionTransformer
    {
        string Name { get; }
        void Transform(ITransformationContext context);
    }

    public interface IConnectionSelector
    {
        IReadOnlyList<Connection> Select(IReadOnlyList<Connection> connections);
    }

    public class TransformationContext : ITransformationContext
    {
        private readonly List<Connection> _connections = new();

        public IReadOnlyList<Connection> Connections => _connections.AsReadOnly();

        public TransformationContext(IEnumerable<Connection> connections)
        {
            _connections.AddRange(connections);
        }

        public void AddConnection(Connection connection)
        {
            _connections.Add(connection);
        }

        public void RemoveConnection(string id)
        {
            _connections.RemoveAll(c => c.Id == id);
        }
    }
}
