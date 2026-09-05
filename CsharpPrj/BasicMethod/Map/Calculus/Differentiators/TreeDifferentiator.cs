namespace BasicMethod.Calculus.Differentiators
{
    public interface ITreeNode<TNode>
        where TNode : ITreeNode<TNode>
    {
        TNode? Parent { get; }
        IReadOnlyCollection<TNode> Children { get; }
    }

    public interface IMutableTreeNode<TNode> : ITreeNode<TNode>
        where TNode : IMutableTreeNode<TNode>
    {
        new IReadOnlyCollection<TNode> Children { get; set; }
    }

    public class TreeDifferentiator<TNode> : DifferentiatorBase<TNode>
        where TNode : ITreeNode<TNode>
    {
        private readonly Func<TNode, bool> _nodeFilter;
        private readonly Func<TNode, TNode> _nodeTransform;
        private readonly Func<TNode, IReadOnlyCollection<TNode>, TNode> _withChildren;

        public TreeDifferentiator(
            string name,
            Func<TNode, bool> nodeFilter,
            Func<TNode, TNode> nodeTransform,
            Func<TNode, IReadOnlyCollection<TNode>, TNode>? withChildren = null)
            : base(name)
        {
            _nodeFilter = nodeFilter ?? throw new ArgumentNullException(nameof(nodeFilter));
            _nodeTransform = nodeTransform ?? throw new ArgumentNullException(nameof(nodeTransform));
            _withChildren = withChildren ?? DefaultWithChildren;
        }

        public override TNode Differentiate(TNode input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            return TransformNode(input);
        }

        private TNode TransformNode(TNode node)
        {
            var transformedChildren = node.Children
                .Select(TransformNode)
                .ToList()
                .AsReadOnly();

            var result = _nodeFilter(node)
                ? _nodeTransform(node)
                : node;

            return _withChildren(result, transformedChildren);
        }

        private static TNode DefaultWithChildren(TNode node, IReadOnlyCollection<TNode> children)
        {
            var childrenProperty = typeof(TNode).GetProperty(nameof(ITreeNode<TNode>.Children));
            if (childrenProperty?.CanWrite == true)
            {
                childrenProperty.SetValue(node, children);
                return node;
            }

            var withMethod = typeof(TNode).GetMethod("<Clone>$");
            if (withMethod != null)
            {
                var clone = (TNode)withMethod.Invoke(node, null);
                if (childrenProperty?.CanWrite == true)
                {
                    childrenProperty.SetValue(clone, children);
                    return clone;
                }
            }

            throw new InvalidOperationException(
                $"Cannot set children on type {typeof(TNode).Name}. " +
                "Either implement IMutableTreeNode<T> with a writable Children property, " +
                "or provide a custom withChildren function.");
        }
    }
}