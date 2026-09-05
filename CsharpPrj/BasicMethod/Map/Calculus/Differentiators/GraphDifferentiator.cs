namespace BasicMethod.Calculus.Differentiators
{
    public interface IGraphNode
    {
        string Id { get; }
    }

    public interface IGraphEdge
    {
        string SourceId { get; }
        string TargetId { get; }
    }

    public interface IGraph<out TNode, out TEdge>
        where TNode : IGraphNode
        where TEdge : IGraphEdge
    {
        IEnumerable<TNode> Nodes { get; }
        IEnumerable<TEdge> Edges { get; }
    }

    public class GraphDifferentiator<TNode, TEdge> : DifferentiatorBase<IGraph<TNode, TEdge>>
        where TNode : IGraphNode
        where TEdge : IGraphEdge
    {
        private readonly Func<TNode, bool> _nodeFilter;
        private readonly Func<TNode, TNode> _nodeTransform;
        private readonly Func<TEdge, bool> _edgeFilter;
        private readonly Func<TEdge, TEdge> _edgeTransform;

        public GraphDifferentiator(string name, Func<TNode, bool> nodeFilter, Func<TNode, TNode> nodeTransform,
            Func<TEdge, bool> edgeFilter, Func<TEdge, TEdge> edgeTransform)
            : base(name)
        {
            _nodeFilter = nodeFilter ?? throw new ArgumentNullException(nameof(nodeFilter));
            _nodeTransform = nodeTransform ?? throw new ArgumentNullException(nameof(nodeTransform));
            _edgeFilter = edgeFilter ?? throw new ArgumentNullException(nameof(edgeFilter));
            _edgeTransform = edgeTransform ?? throw new ArgumentNullException(nameof(edgeTransform));
        }

        public override IGraph<TNode, TEdge> Differentiate(IGraph<TNode, TEdge> input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var transformedNodes = input.Nodes
                .Select(node => _nodeFilter(node) ? _nodeTransform(node) : node)
                .ToList();

            var transformedEdges = input.Edges
                .Select(edge => _edgeFilter(edge) ? _edgeTransform(edge) : edge)
                .ToList();

            return new TransformedGraph<TNode, TEdge>(transformedNodes, transformedEdges);
        }

        private class TransformedGraph<TN, TE> : IGraph<TN, TE>
            where TN : IGraphNode
            where TE : IGraphEdge
        {
            public IEnumerable<TN> Nodes { get; }
            public IEnumerable<TE> Edges { get; }

            public TransformedGraph(IEnumerable<TN> nodes, IEnumerable<TE> edges)
            {
                Nodes = nodes;
                Edges = edges;
            }
        }
    }
}