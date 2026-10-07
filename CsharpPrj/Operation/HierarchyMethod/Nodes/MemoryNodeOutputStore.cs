using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 节点输出端口的内存实现（零持久化）：按「工作流 × 节点」落内存端口表 + 信号元数据表，
    /// 作用域过滤复用单源 <see cref="OutputScopeResolver"/>。供声明式编排入口驱动「挂节点 + 收割产出」，
    /// 以及无宿主设施条件下的内存落点。
    /// <para>与持久实现 <see cref="NodeOutputStore"/>（需 <c>INodeStore</c> 后盾）并列：本类为无宿主设施条件下的
    /// 完整落地（端口值 + 信号元数据），信号枚举排序口径与真实落点一致（sticky → priority → valueScore → freshness，均降序）。</para>
    /// </summary>
    public sealed class MemoryNodeOutputStore : INodeOutputStore
    {
        private readonly Dictionary<(string WorkflowId, string NodeKey), NodeEntry> _entries = new();

        /// <summary>声明式种子：写入一个节点输出端口（null 值忽略，对齐真实存储「不产生空槽位」）。</summary>
        public void Seed(string workflowId, string nodeKey, string port, object? value) => Put(workflowId, nodeKey, port, value);

        /// <summary>种子端口值（同 <see cref="Seed"/>；null 值忽略）。</summary>
        public void Put(string workflowId, string nodeKey, string port, object? value)
        {
            if (value is null) return;
            EntryOf(workflowId, nodeKey).Ports[port] = value;
        }

        /// <summary>种子端口值（兼容入口：与 <see cref="Put"/> 同一实现）。</summary>
        public void Add(string workflowId, string nodeKey, string port, string value) => Put(workflowId, nodeKey, port, value);

        /// <summary>
        /// 种子节点信号元数据（region/module/nodeType/freshness/valueScore/digest/sticky/priority）；
        /// 读-改-写保留既有端口与可见性。
        /// </summary>
        public void PutSignal(string workflowId, string nodeKey, string region, string? module = "task",
            string? nodeType = "task_node", long freshness = 0, double? valueScore = null,
            string? digest = null, bool sticky = false, int priority = 0)
        {
            var entry = EntryOf(workflowId, nodeKey);
            entry.Module = module;
            entry.Region = region;
            entry.NodeType = nodeType;
            entry.Freshness = freshness;
            entry.ValueScore = valueScore;
            entry.Digest = digest;
            entry.Sticky = sticky;
            entry.Priority = priority;
        }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
            => WriteOutputAsync(workflowId, nodeKey, port, value, OutputVisibility.WorkflowLocal, ct);

        public Task WriteOutputAsync(
            string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility,
            CancellationToken ct = default)
        {
            if (value is null) return Task.CompletedTask;

            var entry = EntryOf(workflowId, nodeKey);
            entry.Ports[port] = value;
            entry.Visibility = visibility;
            return Task.CompletedTask;
        }

        public Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
            => Task.FromResult(Snapshot(workflowId, nodeKey));

        public Task<NodeOutput?> TryGetNodeOutputScopedAsync(
            string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default)
        {
            var output = Snapshot(workflowId, nodeKey);
            return Task.FromResult(output is not null
                && OutputScopeResolver.IsVisibleTo(workflowId, output.Visibility, requestingWorkflowId)
                    ? output
                    : null);
        }

        public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeOutput>>(Snapshots(workflowId).ToList());

        public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(
            string workflowId, string requestingWorkflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeOutput>>(Snapshots(workflowId)
                .Where(o => OutputScopeResolver.IsVisibleTo(workflowId, o.Visibility, requestingWorkflowId))
                .ToList());

        public Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_entries.Keys
                .Where(k => string.Equals(k.WorkflowId, workflowId, StringComparison.Ordinal))
                .Select(k => k.NodeKey)
                .ToArray());
        /// <summary>信号元数据写为读-改-写，保留既有端口与可见性。</summary>
        public Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default)
        {
            var entry = EntryOf(workflowId, nodeKey);
            entry.Module = signal.Module;
            entry.Region = signal.Region;
            entry.NodeType = signal.NodeType;
            entry.Freshness = signal.Freshness;
            entry.ValueScore = signal.ValueScore;
            entry.Digest = signal.OutputDigest;
            entry.Sticky = signal.Sticky;
            entry.Priority = signal.Priority;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(
            string workflowId, string? module = null, string? nodeType = null, string? region = null,
            int limit = -1, CancellationToken ct = default)
        {
            var result = _entries
                .Where(kv => string.Equals(kv.Key.WorkflowId, workflowId, StringComparison.Ordinal))
                .Where(kv => string.IsNullOrWhiteSpace(module) || kv.Value.Module == module)
                .Where(kv => string.IsNullOrWhiteSpace(nodeType) || kv.Value.NodeType == nodeType)
                .Where(kv => string.IsNullOrWhiteSpace(region) || kv.Value.Region == region)
                .Select(kv => kv.Value.ToSignal(kv.Key.WorkflowId, kv.Key.NodeKey))
                .OrderByDescending(s => s.Sticky)
                .ThenByDescending(s => s.Priority)
                .ThenByDescending(s => s.ValueScore ?? double.MinValue)
                .ThenByDescending(s => s.Freshness)
                .ToList();

            return Task.FromResult<IReadOnlyList<NodeSignal>>(limit >= 0 ? result.Take(limit).ToList() : result);
        }

        public Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        {
            _entries.Remove((workflowId, nodeKey));
            return Task.CompletedTask;
        }

        public Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default)
        {
            foreach (var key in _entries.Keys
                         .Where(k => string.Equals(k.WorkflowId, workflowId, StringComparison.Ordinal))
                         .ToList())
            {
                _entries.Remove(key);
            }

            return Task.CompletedTask;
        }

        private NodeEntry EntryOf(string workflowId, string nodeKey)
        {
            if (!_entries.TryGetValue((workflowId, nodeKey), out var entry))
                _entries[(workflowId, nodeKey)] = entry = new NodeEntry();

            return entry;
        }

        private NodeOutput? Snapshot(string workflowId, string nodeKey)
            => _entries.TryGetValue((workflowId, nodeKey), out var entry) ? entry.ToOutput(workflowId, nodeKey) : null;

        private IEnumerable<NodeOutput> Snapshots(string workflowId)
            => _entries
                .Where(kv => string.Equals(kv.Key.WorkflowId, workflowId, StringComparison.Ordinal))
                .Select(kv => kv.Value.ToOutput(kv.Key.WorkflowId, kv.Key.NodeKey));

        /// <summary>节点条目：端口表 + 可见性 + 信号元数据（信号字段可变 → 读-改-写保留端口）。</summary>
        private sealed class NodeEntry
        {
            public OutputVisibility Visibility = OutputVisibility.WorkflowLocal;

            public string? Module;

            public string? Region;

            public string? NodeType;

            public string? Digest;

            public long Freshness;

            public double? ValueScore;

            public bool Sticky;

            public int Priority;

            public Dictionary<string, object?> Ports { get; } = new(StringComparer.Ordinal);

            public NodeOutput ToOutput(string workflowId, string nodeKey)
            {
                var output = new NodeOutput
                {
                    NodeKey = nodeKey,
                    WorkflowId = workflowId,
                    Visibility = Visibility,
                    Module = Module,
                    Region = Region,
                    NodeType = NodeType,
                    Freshness = Freshness,
                    ValueScore = ValueScore,
                    OutputDigest = Digest,
                    Sticky = Sticky,
                    Priority = Priority,
                };

                foreach (var (port, value) in Ports)
                    output.OutputPorts[port] = value;

                return output;
            }

            public NodeSignal ToSignal(string workflowId, string nodeKey)
                => new(nodeKey, workflowId, Module, Region, NodeType, Freshness, ValueScore, Digest, Sticky, Priority);
        }
    }
}
