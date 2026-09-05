
using Share.TreeTraceFramework.Models;

namespace Share.TreeTraceFramework.Abstractions
{
    public interface ITraceTree<TNode> : IDisposable
        where TNode : ITraceNode<TNode>
    {
        string TraceId { get; }

        TNode? Root { get; }

        int NodeCount { get; }

        DateTime CreatedAt { get; }


        TNode? FindNode(string nodeId);

        IEnumerable<TNode> FindByName(string name);

        IEnumerable<TNode> FindByLabel(string label);

        IEnumerable<TNode> Query(TraceQuery<TNode> query);

        IEnumerable<TNode> GetAllNodes();


        TNode? FindNearestAncestor(string nodeId, Func<TNode, bool> predicate);

        TNode? FindLowestCommonAncestor(string nodeId1, string nodeId2);

        int GetNodeDepth(string nodeId);

        bool IsDescendantOf(string nodeId, string ancestorNodeId);


        TNode CreateRoot(string name, object? input = null, IEnumerable<string>? labels = null);

        void Clear();
    }
}
