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
    // 组级审计计数（按 GroupKey）：Executed / Waited / Reused（供组结算 NodeReuseStat 汇总，不新增账本）。
    private readonly ConcurrentDictionary<string, UsageCounters> _groupUsage = new(StringComparer.Ordinal);

    private static readonly object SharedGate = new();
    private static NodeLeaseRegistry? _shared;

    public NodeLeaseRegistry(INodeOutputStore outputs, TimeSpan ttl)
    {
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _ttl = ttl;
    }

    /// <summary>进程级共享实例（首个装配者胜出；后续调用忽略参数并返回既有实例）。</summary>
    public static NodeLeaseRegistry GetOrCreate(INodeOutputStore outputs, TimeSpan ttl)
    {
        lock (SharedGate)
        {
            return _shared ??= new NodeLeaseRegistry(outputs, ttl);
        }
    }

    /// <summary>当前共享实例（未装配 ⇒ null；供组审计读取）。</summary>
    public static NodeLeaseRegistry? Shared
    {
        get { lock (SharedGate) return _shared; }
    }

    /// <summary>组级租约使用计数快照（未知组 ⇒ 全 0）。</summary>
    public (int Executed, int Waited, int Reused) GroupUsage(string groupKey)
        => string.IsNullOrWhiteSpace(groupKey) || !_groupUsage.TryGetValue(groupKey, out var counters)
            ? (0, 0, 0)
            : counters.Snapshot();

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
                    // ERR-15：过期不再把节点永久判死——唤醒等待者（可见化）后移除，允许下一个 owner 接管。
                    existing.State = NodeLeaseState.Faulted;
                    existing.Diagnostic = "NODE_LEASE_EXPIRED";
                    existing.Tcs.TrySetResult(Outcome(key, existing, reused: false, waited: false));
                    _leases.TryRemove(key, out _);
                    continue;
                }

                if (existing.State == NodeLeaseState.Completed)
                {
                    Count(existing.GroupKey ?? groupKey, executed: 0, waited: 0, reused: 1);
                    return Outcome(key, existing, reused: true, waited: false);
                }

                if (existing.State == NodeLeaseState.Faulted)
                    return Outcome(key, existing, reused: false, waited: false);

                if (string.Equals(existing.OwnerTaskKey, taskKey, StringComparison.Ordinal))
                    throw new InvalidOperationException($"NODE_LEASE_RECURSIVE: {key.WorkflowId}/{key.NodeKey}/{key.InputDigest}");

                var waited = await existing.Tcs.Task.WaitAsync(ct).ConfigureAwait(false);
                Count(existing.GroupKey ?? groupKey, executed: 0, waited: 1, reused: 0);
                return waited with { Waited = true };
            }

            if (await TryReadCompletedAsync(key, ct).ConfigureAwait(false) is { } completed)
            {
                Count(groupKey, executed: 0, waited: 0, reused: 1);
                return completed;
            }

            var entry = new LeaseEntry
            {
                State = NodeLeaseState.Running,
                OwnerTaskKey = taskKey,
                GroupKey = groupKey,
                StartedAt = DateTimeOffset.UtcNow,
            };
            if (_leases.TryAdd(key, entry))
            {
                Count(groupKey, executed: 1, waited: 0, reused: 0);
                return Outcome(key, entry, reused: false, waited: false);
            }
        }
    }

    /// <summary>发布终态：仅 owner 可发布。返回 false = 非 owner/未知键（调用方须可见化，不再静默）。</summary>
    public bool Publish(NodeLeaseKey key, string taskKey, NodeLeaseState state, string? diagnostic,
        IReadOnlyDictionary<string, object?>? ports = null)
    {
        if (!_leases.TryGetValue(key, out var entry)) return false;
        // 只有 owner 可发布（失败传染也由 owner 发布）；非 owner 发布视为越权。
        if (!string.Equals(entry.OwnerTaskKey, taskKey, StringComparison.Ordinal)) return false;

        entry.State = state;
        entry.Diagnostic = diagnostic;
        entry.Ports = ports;
        entry.Tcs.TrySetResult(Outcome(key, entry, reused: false, waited: false));
        return true;
    }

    private void Count(string? groupKey, int executed, int waited, int reused)
    {
        if (string.IsNullOrWhiteSpace(groupKey)) return;
        var counters = _groupUsage.GetOrAdd(groupKey!, static _ => new UsageCounters());
        Interlocked.Add(ref counters.Executed, executed);
        Interlocked.Add(ref counters.Waited, waited);
        Interlocked.Add(ref counters.Reused, reused);
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

    private sealed class UsageCounters
    {
        public int Executed;
        public int Waited;
        public int Reused;

        public (int Executed, int Waited, int Reused) Snapshot()
            => (Volatile.Read(ref Executed), Volatile.Read(ref Waited), Volatile.Read(ref Reused));
    }

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
