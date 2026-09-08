#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 上层适配器（S5）：把底层节点输出存储端口 <see cref="INodeOutputStore"/>（ur_nodes/SQLite）
    /// 映射为框架级 <see cref="INodeProcessOutputStore"/>，供 WorkFlowMethod.NodeProcess 管线以
    /// 「节点即存储单元」持久化/检索节点产出。
    /// 依赖方向：HierarchyMethod（上层）→ 实现 WorkFlowMethod 的自持最小抽象接口（底层），无反向引用、无环。
    /// 去 Agent 依赖：本适配器只做模型映射（NodeOutput → NodeOutputResult），不含任何 Agent/LLM 逻辑。
    /// </summary>
    public sealed class NodeProcessOutputStoreAdapter : INodeProcessOutputStore
    {
        private readonly INodeOutputStore _inner;

        public NodeProcessOutputStoreAdapter(INodeOutputStore inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
            => _inner.WriteOutputAsync(workflowId, nodeKey, port, value, ct);

        public async Task<NodeOutputResult?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        {
            var node = await _inner.TryGetNodeOutputAsync(workflowId, nodeKey, ct).ConfigureAwait(false);
            return node is null ? null : Map(node);
        }

        public async Task<IReadOnlyList<NodeOutputResult>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default)
        {
            var nodes = await _inner.EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false);
            return nodes.Select(Map).ToList();
        }

        private static NodeOutputResult Map(NodeOutput node)
        {
            var result = new NodeOutputResult
            {
                NodeKey = node.NodeKey,
                WorkflowId = node.WorkflowId,
            };
            foreach (var (port, value) in node.OutputPorts)
                result.OutputPorts[port] = value;
            return result;
        }
    }
}