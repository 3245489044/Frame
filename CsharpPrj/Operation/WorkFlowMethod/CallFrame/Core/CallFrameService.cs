
using System.Collections.Concurrent;
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;
using Share.TreeTraceFramework.Models;
using WorkFlowMethod.CallFrame.Abstractions;

namespace WorkFlowMethod.CallFrame.Core
{
    public class CallFrameService : TraceService, ICallFrameService
    {
        private readonly ConcurrentDictionary<string, ICallFrame> _callFrames = new();

        public static new CallFrameService Default { get; } = new CallFrameService();

        public CallFrameService()
            : base(sessionId => new CallFrame(sessionId)) { }


        public ICallFrame CreateCallFrame(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentNullException(nameof(sessionId));

            var callFrame = new CallFrame(sessionId);
            _callFrames[sessionId] = callFrame;
            CreateTraceTree(sessionId);
            return callFrame;
        }

        public ICallFrame? GetCallFrame(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            _callFrames.TryGetValue(sessionId, out var callFrame);
            return callFrame;
        }

        public ICallFrame GetOrCreateCallFrame(string sessionId)
        {
            return GetCallFrame(sessionId) ?? CreateCallFrame(sessionId);
        }

        public bool DeleteCallFrame(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (_callFrames.TryRemove(sessionId, out var callFrame))
            {
                callFrame.Dispose();
                DeleteTraceTree(sessionId);
                return true;
            }
            return false;
        }


        public new ICallFrameNode? AddNodeFromConfig(string sessionId, AddNodeConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var callFrame = GetOrCreateCallFrame(sessionId);

            if (!string.IsNullOrEmpty(config.ParentNodeId))
            {
                var parent = callFrame.FindNode(config.ParentNodeId);
                if (parent != null)
                {
                    var child = parent.AddChild(config.Name);
                    child.AddInput(config.Inputs.FirstOrDefault());
                    child.AddOutput(config.Outputs.FirstOrDefault());
                    foreach (var label in config.Labels) child.AddLabel(label);
                    foreach (var record in config.Records) child.AddRecord(record);
                    return (ICallFrameNode)child;
                }
                return null;
            }

            if (callFrame.Root == null)
            {
                var root = callFrame.CreateRoot(config.Name);
                if (config.Inputs.FirstOrDefault() != null) root.AddInput(config.Inputs.FirstOrDefault());
                if (config.Outputs.FirstOrDefault() != null) root.AddOutput(config.Outputs.FirstOrDefault());
                foreach (var label in config.Labels) root.AddLabel(label);
                foreach (var record in config.Records) root.AddRecord(record);
                return root;
            }

            var childNode = callFrame.Root.AddChild(config.Name);
            if (config.Inputs.FirstOrDefault() != null) childNode.AddInput(config.Inputs.FirstOrDefault());
            if (config.Outputs.FirstOrDefault() != null) childNode.AddOutput(config.Outputs.FirstOrDefault());
            foreach (var label in config.Labels) childNode.AddLabel(label);
            foreach (var record in config.Records) childNode.AddRecord(record);
            return (ICallFrameNode)childNode;
        }


        public new IEnumerable<ICallFrameNode> QueryNodes(string sessionId, TraceQuery<TraceNode> query)
        {
            var callFrame = GetCallFrame(sessionId);
            if (callFrame == null)
                return Enumerable.Empty<ICallFrameNode>();

            return callFrame.Query(query);
        }


        public void StoreCallFrame(ICallFrame callFrame)
        {
            if (callFrame == null)
                throw new ArgumentNullException(nameof(callFrame));

            var existingEntry = _callFrames.FirstOrDefault(kvp => kvp.Value.CallFrameId == callFrame.CallFrameId);
            if (!existingEntry.Equals(default(KeyValuePair<string, ICallFrame>)))
            {
                _callFrames[existingEntry.Key] = callFrame;
            }
            StoreTraceTree(callFrame);
        }

        public ICallFrame? RestoreCallFrame(string callFrameId)
        {
            if (string.IsNullOrEmpty(callFrameId))
                return null;

            return _callFrames.Values.FirstOrDefault(cf => cf.CallFrameId == callFrameId);
        }
    }
}
