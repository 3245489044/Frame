#region USINGS
using System;
using System.Collections.Generic;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Snapshots;
using WorkFlowMethod.Orchestration.Context.Models;
#endregion

namespace WorkFlowMethod.WorkFlowPipeline
{
    public class PipelineContextAccessor
    {
        private readonly PipelineContext _context;

        public PipelineContextAccessor(PipelineContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public PipelineContext Context => _context;


        public ExecutionSession? Session => _context.Session as ExecutionSession;

        public DataContext? Data => Session?.Data;

        public NodeExecutionState? GetOrCreateNodeState(string nodeKey)
            => Session?.GetOrCreateNodeState(nodeKey);

        public NodeExecutionState? GetNodeState(string nodeKey)
            => Session?.GetNodeState(nodeKey);


        public GraphSnapshot<string, NodeConfig, object>? Snapshot => _context.GraphSnapshot as GraphSnapshot<string, NodeConfig, object>;

        public string? SnapshotId => Snapshot?.SnapshotId;

        public string? GraphId => Snapshot?.GraphId;

        public int Version => Snapshot?.Version ?? 0;

        public IReadOnlyList<string> NodeKeys
            => Snapshot?.NodeKeys ?? Array.Empty<string>();

        public IReadOnlyDictionary<string, NodeConfig>? Nodes => Snapshot?.Nodes;

        public IReadOnlyList<SnapshotEdge<string>>? Edges => Snapshot?.Edges;

        public NodeConfig? GetNodeData(string nodeKey)
            => Snapshot?.GetNodeData(nodeKey);

        public NodeConfig? GetCurrentNodeData()
        {
            if (string.IsNullOrEmpty(_context.CurrentNodeKey))
                return null;
            return GetNodeData(_context.CurrentNodeKey);
        }

        public string? GetCurrentNodeSymbol()
            => GetNodeData(_context.CurrentNodeKey)?.Symbol;

        public T? GetInput<T>(string? key = null) where T : class
        {
            var inputKey = key ?? $"{_context.CurrentNodeKey}.Input";
            return Data?.Get<T>(inputKey);
        }

        public void SetOutput(object output, string? key = null)
        {
            var outputKey = key ?? $"{_context.CurrentNodeKey}.Output";
            Data?.Set(outputKey, output);
            _context.CurrentOutput = output;
        }

        public CallFrameRecord? GetCallFrame(string? nodeKey = null)
        {
            var key = nodeKey ?? _context.CurrentNodeKey;
            if (string.IsNullOrEmpty(key)) return null;
            return Data?.Get<CallFrameRecord>($"CallFrame.{key}");
        }

        public void RecordCallFrame(object? input = null, object? output = null)
        {
            var currentNodeKey = _context.CurrentNodeKey;
            if (string.IsNullOrEmpty(currentNodeKey)) return;

            var currentNodeData = GetCurrentNodeData();
            Data?.Set($"CallFrame.{currentNodeKey}", new CallFrameRecord
            {
                CallKey = currentNodeKey,
                Symbol = currentNodeData?.Symbol ?? "Unknown",
                Status = "Completed",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Inputs = new Dictionary<string, object>
                {
                    ["input"] = input ?? _context.CurrentInput ?? new object()
                },
                Outputs = new Dictionary<string, object>
                {
                    ["output"] = output ?? _context.CurrentOutput ?? new object()
                }
            });
        }
    }
}
