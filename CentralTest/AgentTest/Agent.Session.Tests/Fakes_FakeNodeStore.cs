using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;

namespace Agent.Session.Tests;

// ---- 三粒度构建器测试用内存假端口（只实现测试路径；未命中返回空）----

internal sealed class FakeNodeStore : INodeOutputStore
{
    private readonly Dictionary<(string, string), NodeOutput> _outputs = new();

    public void Put(string workflowId, string nodeKey, string port, object? value)
    {
        if (!_outputs.TryGetValue((workflowId, nodeKey), out var o))
        {
            o = new NodeOutput { NodeKey = nodeKey, WorkflowId = workflowId };
            _outputs[(workflowId, nodeKey)] = o;
        }
        o.OutputPorts[port] = value;
    }

    /// <summary>写入节点信号元数据（region/module/nodeType/freshness/valueScore/digest/sticky/priority），供区域化构建测试。
    /// NodeOutput 信号字段为 init-only，故重建实例（保留既有端口）。</summary>
    public void PutSignal(string workflowId, string nodeKey, string region, string? module = "task",
        string? nodeType = "task_node", long freshness = 0, double? valueScore = null,
        string? digest = null, bool sticky = false, int priority = 0)
    {
        _outputs.TryGetValue((workflowId, nodeKey), out var existing);
        var o = new NodeOutput
        {
            NodeKey = nodeKey,
            WorkflowId = workflowId,
            Module = module,
            Region = region,
            NodeType = nodeType,
            Freshness = freshness,
            ValueScore = valueScore,
            OutputDigest = digest,
            Sticky = sticky,
            Priority = priority,
        };
        if (existing is not null)
            foreach (var (port, v) in existing.OutputPorts)
                o.OutputPorts[port] = v;
        _outputs[(workflowId, nodeKey)] = o;
    }

    public Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        => Task.FromResult(_outputs.TryGetValue((workflowId, nodeKey), out var o) ? o : null);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default) { Put(workflowId, nodeKey, port, value); return Task.CompletedTask; }
    public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility, CancellationToken ct = default) { Put(workflowId, nodeKey, port, value); return Task.CompletedTask; }
    public Task<NodeOutput?> TryGetNodeOutputScopedAsync(string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default) => TryGetNodeOutputAsync(workflowId, nodeKey, ct);
    public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NodeOutput>>(_outputs.Where(kv => kv.Key.Item1 == workflowId).Select(kv => kv.Value).ToList());
    public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(string workflowId, string requestingWorkflowId, CancellationToken ct = default) => EnumerateOutputsAsync(workflowId, ct);
    public Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(_outputs.Where(kv => kv.Key.Item1 == workflowId).Select(kv => kv.Key.Item2).ToList());
    public Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default) => Task.FromResult(signal);
    public Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(string workflowId, string? module = null, string? nodeType = null, string? region = null, int limit = -1, CancellationToken ct = default)
    {
        var result = _outputs
            .Where(kv => kv.Key.Item1 == workflowId)
            .Where(kv => string.IsNullOrWhiteSpace(module) || kv.Value.Module == module)
            .Where(kv => string.IsNullOrWhiteSpace(nodeType) || kv.Value.NodeType == nodeType)
            .Where(kv => string.IsNullOrWhiteSpace(region) || kv.Value.Region == region)
            .Select(kv => new NodeSignal(kv.Value.NodeKey, kv.Value.WorkflowId, kv.Value.Module, kv.Value.Region, kv.Value.NodeType, kv.Value.Freshness, kv.Value.ValueScore, kv.Value.OutputDigest, kv.Value.Sticky, kv.Value.Priority))
            .OrderByDescending(s => s.Sticky).ThenByDescending(s => s.Priority).ThenByDescending(s => s.ValueScore ?? double.MinValue).ThenByDescending(s => s.Freshness)
            .ToList();
        return Task.FromResult<IReadOnlyList<NodeSignal>>(limit >= 0 ? result.Take(limit).ToList() : result);
    }
    public Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default) { _outputs.Remove((workflowId, nodeKey)); return Task.CompletedTask; }
    public Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default) { foreach (var k in _outputs.Keys.Where(k => k.Item1 == workflowId).ToList()) _outputs.Remove(k); return Task.CompletedTask; }
}
