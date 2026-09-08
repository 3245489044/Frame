#region USINGS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Abstractions
{
    /// <summary>
    /// 节点输出存储端口（框架级最小抽象）——把节点结果按「工作流节点」为基底持久化与检索。
    /// 节点即存储单元：<see cref="WriteOutputAsync"/> 落盘节点端口，<see cref="EnumerateOutputsAsync"/> /
    /// <see cref="TryGetNodeOutputAsync"/> 提供按节点维度组合与使用。
    /// 本接口为 NodeProcess 自持最小契约（去 Agent/上层依赖）；真实 SQLite 实现由上层
    /// （HierarchyMethod.Nodes.NodeOutputStore / ur_nodes）经适配器对接，依赖方向保持：WorkFlowMethod ← HierarchyMethod。
    /// </summary>
    public interface INodeProcessOutputStore
    {
        /// <summary>写入一个节点输出端口（null 值忽略，不产生空槽位）。</summary>
        Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default);

        /// <summary>取单个节点的完整输出（<see cref="NodeOutputResult"/>）；节点无输出返回 null。</summary>
        Task<NodeOutputResult?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>枚举工作流下全部节点输出（供 KeyMapping/ReadView 组合源键空间）。</summary>
        Task<IReadOnlyList<NodeOutputResult>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default);
    }
}