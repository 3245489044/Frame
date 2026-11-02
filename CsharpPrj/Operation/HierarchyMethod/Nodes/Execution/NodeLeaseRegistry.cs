using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点租约注册表（L3 · 单飞）：同 (workflowId,nodeKey,inputDigest) 恰一个执行者；
/// 其余等待同一结果（组内/跨组一致）。跨组只复用「已完成且 digest 相同」的产物。
/// <para>内存条目承担在跑单飞；已完成产物以 <c>ur_nodes</c> 的 InputDigest 为跨进程复用依据。</para>
/// </summary>
public sealed class NodeLeaseRegistry : INodeLeaseRegistry
{
    private readonly INodeOutputStore _outputs;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<NodeLeaseKey, LeaseEntry> _leases = new();

    public NodeLeaseRegistry(INodeOutputStore outputs, TimeSpan ttl)
    {
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _ttl = ttl;
    }

    public async Task<NodeLeaseOutcome> AcquireOrWaitAsync(
        NodeLeaseKey key, string taskKey, string? groupKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_leases.TryGetValue(key, out var existing))
            {
                if (existing.State == NodeLeaseState.Running && IsExpired(existing))
                {
                    existing.State = NodeLeaseState.Faulted;
                    existing.Diagnostic = "expired";
                    existing.Tcs.TrySetResult(Outcome(key, existing, reused: false, waited: false));
                    _leases.TryRemove(key, out _);
                    continue;
                }

                if (existing.State == NodeLeaseState.Completed)
                    return Outcome(key, existing, reused: true, waited: false);

                if (existing.State == NodeLeaseState.Faulted)
                    return Outcome(key, existing, reused: false, waited: false);

                if (string.Equals(existing.OwnerTaskKey, taskKey, StringComparison.Ordinal))
                    throw new InvalidOperationException($"NODE_LEASE_RECURSIVE: {key.WorkflowId}/{key.NodeKey}/{key.InputDigest}");

                var waited = await existing.Tcs.Task.WaitAsync(ct).ConfigureAwait(false);
                return waited with { Waited = true };
            }

            if (await TryReadCompletedAsync(key, ct).ConfigureAwait(false) is { } completed)
                return completed;

            var entry = new LeaseEntry
            {
                State = NodeLeaseState.Running,
                OwnerTaskKey = taskKey,
                GroupKey = groupKey,
                StartedAt = DateTimeOffset.UtcNow,
            };
            if (_leases.TryAdd(key, entry))
                return Outcome(key, entry, reused: false, waited: false);
        }
    }

    public void Publish(NodeLeaseKey key, string taskKey, NodeLeaseState state, string? diagnostic,
        IReadOnlyDictionary<string, object?>? ports = null)
    {
        if (!_leases.TryGetValue(key, out var entry)) return;
        // 只有 owner 可发布（失败传染也由 owner 发布）；非 owner 发布视为越权，静默拒绝（可见化由诊断入口承担）。
        if (!string.Equals(entry.OwnerTaskKey, taskKey, StringComparison.Ordinal)) return;

        entry.State = state;
        entry.Diagnostic = diagnostic;
        entry.Ports = ports;
        entry.Tcs.TrySetResult(Outcome(key, entry, reused: false, waited: false));
    }

    private async Task<NodeLeaseOutcome?> TryReadCompletedAsync(NodeLeaseKey key, CancellationToken ct)
    {
        var output = await _outputs.TryGetNodeOutputAsync(key.WorkflowId, key.NodeKey, ct).ConfigureAwait(false);
        if (output is null) return null;
        if (!string.Equals(output.InputDigest, key.InputDigest, StringComparison.Ordinal)) return null;
        if (output.OutputPorts.Count == 0) return null;

        var ports = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in output.OutputPorts) ports[pair.Key] = pair.Value;
        return new NodeLeaseOutcome(key, NodeLeaseState.Completed, Reused: true, Waited: false,
            OwnerTaskKey: null, Diagnostic: null, Ports: ports);
    }

    private bool IsExpired(LeaseEntry entry)
        => _ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - entry.StartedAt > _ttl;

    private static NodeLeaseOutcome Outcome(NodeLeaseKey key, LeaseEntry entry, bool reused, bool waited)
        => new(key, entry.State, reused, waited, entry.OwnerTaskKey, entry.Diagnostic, entry.Ports);

    private sealed class LeaseEntry
    {
        public NodeLeaseState State;
        public string? OwnerTaskKey;
        public string? GroupKey;
        public string? Diagnostic;
        public IReadOnlyDictionary<string, object?>? Ports;
        public DateTimeOffset StartedAt;
        public TaskCompletionSource<NodeLeaseOutcome> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
