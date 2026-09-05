
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;
using Share.TreeTraceFramework.Models;
using WorkFlowMethod.CallFrame.Abstractions;

namespace WorkFlowMethod.CallFrame.Core
{
    public class CallFrame : TraceTree, ICallFrame
    {
        public string CallFrameId => TraceId;

        public string SessionId { get; }

        public new ICallFrameNode? Root => (ICallFrameNode?)base.Root;

        public CallFrame(string sessionId, string? traceId = null, ITraceQueryEngine<TraceNode>? queryEngine = null)
            : base(traceId, queryEngine)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        }

        protected override TraceNode CreateNodeInstance(string name, string? nodeId = null)
        {
            return new CallFrameNode(name, this, nodeId);
        }


        public new ICallFrameNode? FindNode(string nodeId)
        {
            return (ICallFrameNode?)base.FindNode(nodeId);
        }

        public IEnumerable<ICallFrameNode> FindBySymbol(string symbol)
        {
            return FindByName(symbol).Cast<ICallFrameNode>();
        }

        public new IEnumerable<ICallFrameNode> FindByLabel(string label)
        {
            return base.FindByLabel(label).Cast<ICallFrameNode>();
        }

        public new IEnumerable<ICallFrameNode> Query(TraceQuery<TraceNode> query)
        {
            return base.Query(query).Cast<ICallFrameNode>();
        }

        public new IEnumerable<ICallFrameNode> GetAllNodes()
        {
            return base.GetAllNodes().Cast<ICallFrameNode>();
        }


        public new ICallFrameNode? FindNearestAncestor(string nodeId, Func<TraceNode, bool> predicate)
        {
            return (ICallFrameNode?)base.FindNearestAncestor(nodeId, predicate);
        }

        public new ICallFrameNode? FindLowestCommonAncestor(string nodeId1, string nodeId2)
        {
            return (ICallFrameNode?)base.FindLowestCommonAncestor(nodeId1, nodeId2);
        }


        public new ICallFrameNode CreateRoot(string symbol, object? input = null, IEnumerable<string>? labels = null)
        {
            return (ICallFrameNode)base.CreateRoot(symbol, input, labels);
        }

        public override string ToString()
        {
            return $"CallFrame [Session: {SessionId}, TraceId: {TraceId[..8]}, Nodes: {NodeCount}]";
        }
    }
}
