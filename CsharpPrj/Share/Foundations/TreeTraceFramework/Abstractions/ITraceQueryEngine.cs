
using Share.TreeTraceFramework.Models;

namespace Share.TreeTraceFramework.Abstractions
{
    public interface ITraceQueryEngine<TNode>
        where TNode : ITraceNode<TNode>
    {
        IEnumerable<TNode> Execute(ITraceTree<TNode> tree, TraceQuery<TNode> query);

        IEnumerable<TNode> Execute(IEnumerable<TNode> nodes, TraceQuery<TNode> query);
    }
}
