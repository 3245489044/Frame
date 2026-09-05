
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;
using Share.TreeTraceFramework.Models;
using WorkFlowMethod.CallFrame.Models;

namespace WorkFlowMethod.CallFrame.Abstractions
{
    public interface ICallFrame : ITraceTree<TraceNode>
    {
        string CallFrameId { get; }

        string SessionId { get; }

        new ICallFrameNode? Root { get; }


        new ICallFrameNode? FindNode(string nodeId);

        IEnumerable<ICallFrameNode> FindBySymbol(string symbol);

        new IEnumerable<ICallFrameNode> FindByLabel(string label);

        new IEnumerable<ICallFrameNode> Query(TraceQuery<TraceNode> query);

        new IEnumerable<ICallFrameNode> GetAllNodes();


        new ICallFrameNode? FindNearestAncestor(string nodeId, Func<TraceNode, bool> predicate);

        new ICallFrameNode? FindLowestCommonAncestor(string nodeId1, string nodeId2);


        new ICallFrameNode CreateRoot(string symbol, object? input = null, IEnumerable<string>? labels = null);
    }
}
