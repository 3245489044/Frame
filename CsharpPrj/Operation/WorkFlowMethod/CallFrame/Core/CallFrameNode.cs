
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;
using WorkFlowMethod.CallFrame.Abstractions;

namespace WorkFlowMethod.CallFrame.Core
{
    public class CallFrameNode : TraceNode, ICallFrameNode
    {
        public string Symbol => Name;

        public CallFrameNode(string symbol, ITraceTree<TraceNode> owner, string? nodeId = null)
            : base(symbol, owner, nodeId) { }

        public new ICallFrameNode AddChild(string symbol, object? input = null, object? output = null, IEnumerable<string>? labels = null)
        {
            return (ICallFrameNode)base.AddChild(symbol, input, output, labels);
        }

        public override string ToString()
        {
            return $"[{NodeId[..8]}] {Symbol} (Depth: {Depth})";
        }
    }
}
