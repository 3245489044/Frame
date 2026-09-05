
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;

namespace WorkFlowMethod.CallFrame.Abstractions
{
    public interface ICallFrameNode : ITraceNode<TraceNode>
    {
        string Symbol { get; }

        new ICallFrameNode AddChild(string symbol, object? input = null, object? output = null, IEnumerable<string>? labels = null);
    }
}
