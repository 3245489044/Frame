using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 节点输出端口的内存实现（最小实现，零持久化）：按「工作流 × 节点」落内存端口表，
    /// 作用域过滤复用单源 <see cref="OutputScopeResolver"/>。供声明式编排入口驱动「挂节点 + 收割产出」。
    /// <para>与持久实现 <see cref="NodeOutputStore"/>（需 <c>INodeStore</c> 后盾）并列：本类为无宿主设施条件下的
    /// 最小落地（原 Agent 侧 TaskPool 的 DeclaredNodeOutputStore 下沉，SingleImplementation / TH-12）。</para>
    /// <para>信号元数据面（<see cref="WriteSignalMetadataAsync"/> / <see cref="EnumerateSignalsAsync"/>）
    /// 不在本内存端口承载范围：显式抛 <see cref="NotSupportedException"/>（不静默降级，需要时另装配真实落点）。</para>
    /// </summary>
    public sealed class MemoryNodeOutputStore : INodeOutputStore
    {
        private readonly Dictionary<(string WorkflowId, string NodeKey), NodeEntry> _entries = new();

        /// <summary>声明式种子：写入一个节点输出端口（null 值忽略，对齐真实存储「不产生空槽位」）。</summary>
        public void Seed(string workflowId, string nodeKey, string port, object? value)
        {
            if (value is null) return;
            EntryOf(workflowId, nodeKey).Ports[port] = value;
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
        public Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default)
            => throw new NotSupportedException("声明式内存节点输出端口不承载信号元数据写入（请装配真实落点）");

        public Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(
            string workflowId, string? module = null, string? nodeType = null, string? region = null,
            int limit = -1, CancellationToken ct = default)
            => throw new NotSupportedException("声明式内存节点输出端口不承载信号摘要枚举（请装配真实落点）");

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

        /// <summary>节点条目：端口表 + 可见性；读取时投影为不可变 <see cref="NodeOutput"/> 快照。</summary>
        private sealed class NodeEntry
        {
            public OutputVisibility Visibility = OutputVisibility.WorkflowLocal;

            public Dictionary<string, object?> Ports { get; } = new(StringComparer.Ordinal);

            public NodeOutput ToOutput(string workflowId, string nodeKey)
            {
                var output = new NodeOutput { NodeKey = nodeKey, WorkflowId = workflowId, Visibility = Visibility };
                foreach (var (port, value) in Ports)
                    output.OutputPorts[port] = value;

                return output;
            }
        }
    }
}
