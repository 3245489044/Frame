using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;

namespace Agent.Core.Tests.TestSupport
{
    /// <summary>测试用节点输出桩（INodeOutputStore 内存实现）：Add 种子端口值，供 GetNodesAsync 从 ur_nodes 重建分层节点。</summary>
    internal sealed class FakeNodeOutputStore : INodeOutputStore
    {
        private readonly Dictionary<string, NodeOutput> _outputs = new(StringComparer.Ordinal);

        public void Add(string workflowId, string nodeKey, string port, string value)
        {
            var key = workflowId + "." + nodeKey;
            if (!_outputs.TryGetValue(key, out var output))
            {
                output = new NodeOutput { NodeKey = nodeKey, WorkflowId = workflowId };
                _outputs[key] = output;
            }
            output.OutputPorts[port] = value;
        }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default) => Task.CompletedTask;
        public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility, CancellationToken ct = default) => Task.CompletedTask;
        public Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
            => Task.FromResult(_outputs.TryGetValue(workflowId + "." + nodeKey, out var output) ? output : null);
        public Task<NodeOutput?> TryGetNodeOutputScopedAsync(string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default)
            => TryGetNodeOutputAsync(workflowId, nodeKey, ct);
        public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeOutput>>(_outputs.Values.Where(o => o.WorkflowId == workflowId).ToList());
        public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(string workflowId, string requestingWorkflowId, CancellationToken ct = default)
            => EnumerateOutputsAsync(workflowId, ct);
        public Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_outputs.Values.Where(o => o.WorkflowId == workflowId).Select(o => o.NodeKey).ToList());
        public Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default)
            => Task.FromResult(signal);
        public Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(string workflowId, string? module = null, string? nodeType = null, string? region = null, int limit = -1, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeSignal>>(_outputs.Values.Where(o => o.WorkflowId == workflowId).Select(o => new NodeSignal(o.NodeKey, o.WorkflowId, o.Module, o.Region, o.NodeType, o.Freshness, o.ValueScore, o.OutputDigest, o.Sticky, o.Priority)).ToList());
        public Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
