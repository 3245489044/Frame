#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Persistence
{
    /// <summary>
    /// 内存版 <see cref="INodeProcessOutputStore"/>（S1 骨架测试实现；S5 由上层的 SQLite 适配器替换）。
    /// 线程安全：并发写入经 NodeOutputResult.OutputPorts 锁串行；读返回节点实例（以只读消费约定）。
    /// </summary>
    public sealed class InMemoryNodeProcessOutputStore : INodeProcessOutputStore
    {
        private readonly ConcurrentDictionary<string, NodeOutputResult> _nodes = new(StringComparer.Ordinal);

        private static string Key(string workflowId, string nodeKey) => workflowId + '\u0001' + nodeKey;

        public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
        {
            if (value is null) return Task.CompletedTask; // null 空槽位不产生
            var key = Key(workflowId, nodeKey);
            var node = _nodes.GetOrAdd(key, _ => new NodeOutputResult { WorkflowId = workflowId, NodeKey = nodeKey });
            lock (node.OutputPorts) node.OutputPorts[port] = value;
            return Task.CompletedTask;
        }

        public Task<NodeOutputResult?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
            => Task.FromResult(_nodes.TryGetValue(Key(workflowId, nodeKey), out var node) ? node : null);

        public Task<IReadOnlyList<NodeOutputResult>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default)
        {
            var prefix = workflowId + '\u0001';
            var list = _nodes
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .OrderBy(n => n.NodeKey, StringComparer.Ordinal) // 确定性枚举顺序：裸端口「先到者胜」契约稳定
                .ToList();
            return Task.FromResult<IReadOnlyList<NodeOutputResult>>(list);
        }
    }
}