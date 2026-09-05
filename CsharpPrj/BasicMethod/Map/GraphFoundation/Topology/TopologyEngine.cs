
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Topology.Transformers;

namespace GraphFoundation.Topology
{
    public class TopologyEngine<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly List<IConnectionTransformer> _transformers = new();

        public TopologyEngine(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public void AddTransformer(IConnectionTransformer transformer)
        {
            _transformers.Add(transformer);
        }

        public void Execute()
        {
            var connections = new List<Connection>();
            foreach (var edgeId in _graph.GetAllEdgeIds())
            {
                var source = _graph.GetEdgeSource(edgeId);
                var target = _graph.GetEdgeTarget(edgeId);
                var weight = _graph.GetEdgeWeight(edgeId);
                connections.Add(new Connection(edgeId, source.ToString()!, target.ToString()!, true, weight));
            }

            var context = new TransformationContext(connections);
            foreach (var transformer in _transformers)
            {
                transformer.Transform(context);
            }
        }
    }

    public class GraphBuilder<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly Func<string, TKey> _keyFactory;
        private readonly Func<TKey, TNodeData> _nodeFactory;
        private readonly Func<TKey, TKey, TEdgeData> _edgeFactory;

        public GraphBuilder(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<string, TKey> keyFactory,
            Func<TKey, TNodeData> nodeFactory,
            Func<TKey, TKey, TEdgeData> edgeFactory)
        {
            _graph = graph;
            _keyFactory = keyFactory;
            _nodeFactory = nodeFactory;
            _edgeFactory = edgeFactory;
        }

        public void BuildFromConnections(IEnumerable<Connection> connections)
        {
            foreach (var conn in connections)
            {
                var sourceKey = _keyFactory(conn.Source);
                var targetKey = _keyFactory(conn.Target);

                if (!_graph.ContainsNode(sourceKey))
                    _graph.AddNode(sourceKey, _nodeFactory(sourceKey));
                if (!_graph.ContainsNode(targetKey))
                    _graph.AddNode(targetKey, _nodeFactory(targetKey));
                if (!_graph.ContainsEdge(conn.Id))
                    _graph.AddEdge(conn.Id, sourceKey, targetKey, conn.IsDirected, _edgeFactory(sourceKey, targetKey), conn.Weight);
            }
        }
    }

}
