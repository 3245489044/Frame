
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;
using Share.TreeTraceFramework.Models;

namespace WorkFlowMethod.CallFrame.Abstractions
{
    public interface ICallFrameService : ITraceService<TraceNode>
    {
        ICallFrame CreateCallFrame(string sessionId);

        ICallFrame? GetCallFrame(string sessionId);

        ICallFrame GetOrCreateCallFrame(string sessionId);

        bool DeleteCallFrame(string sessionId);

        new ICallFrameNode? AddNodeFromConfig(string sessionId, AddNodeConfig config);

        new IEnumerable<ICallFrameNode> QueryNodes(string sessionId, TraceQuery<TraceNode> query);

        void StoreCallFrame(ICallFrame callFrame);

        ICallFrame? RestoreCallFrame(string callFrameId);
    }
}
