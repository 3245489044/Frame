using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 节点输出存储端口 —— 把 Agent 所有输出按「工作流节点」为基底持久化与检索。
    /// 节点即存储单元：WriteOutput 落盘节点端口，Enumerate/TryGet 提供按节点维度组合与使用。
    /// 存储收敛（模块 A 并入模块 C，见「节点即存储单元」架构）：落点 = 基础节点库 ur_nodes（NodeType=output），
    /// 每个节点一条记录（NodeKey = nodes.{workflowId}.{nodeKey}），端口以 JSON 载荷承载、值经类型保真往返，
    /// 数据载荷持久化、重启可恢复，不再依赖 StateCenter Session 层内存（消除「存储基底不落盘」缺陷）。
    /// </summary>
    public interface INodeOutputStore
    {
        /// <summary>初始化（ur_nodes 由基础节点库幂等建表，本端口通常为 no-op）。</summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>写入一个节点输出端口（null 值忽略，不产生空槽位）。</summary>
        Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default);

        /// <summary>写入一个节点输出端口（null 值忽略），并声明节点可见性（对齐 C# 作用域/public，跨工作流访问须 Exported）。</summary>
        Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility, CancellationToken ct = default);

        /// <summary>取单个节点的完整输出（NodeOutput）；节点无输出返回 null。</summary>
        Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>取单个节点输出，并按请求工作流作用域过滤（跨工作流仅 Exported 可见；同工作流恒可见）。不可见/无输出返回 null。</summary>
        Task<NodeOutput?> TryGetNodeOutputScopedAsync(string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default);

        /// <summary>枚举工作流下全部节点输出（供 KeyMapping 组合源键空间）。</summary>
        Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default);

        /// <summary>枚举工作流下节点输出，并按请求工作流作用域过滤（跨工作流仅 Exported 可见）。</summary>
        Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(string workflowId, string requestingWorkflowId, CancellationToken ct = default);

        /// <summary>枚举工作流下已产出输出的节点键。</summary>
        Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default);

        /// <summary>写入/更新单个节点的信号元数据（module/region/nodeType/freshness/valueScore/outputDigest/sticky/priority）。
        /// 读-改-写保留既有输出端口；仅更新节点信号字段。供「节点落库标注 module」与信号摘要写入。</summary>
        Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default);

        /// <summary>枚举工作流下全部节点信号摘要（NodeSignal；免读正文，只读信号元数据 + 摘要）。
        /// 供 read_nodes / 区域化上下文构建按信号评判最近/价值/区域。可空过滤器 <paramref name="module"/> /
        /// <paramref name="nodeType"/> / <paramref name="region"/> 均按精确匹配过滤；全部为空 = 枚举全量。
        /// 返回按信号 rank 排序（sticky → valueScore desc → freshness desc）；<paramref name="limit"/> 上限截断（缺省 -1 = 不限）。</summary>
        Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(
            string workflowId,
            string? module = null,
            string? nodeType = null,
            string? region = null,
            int limit = -1,
            CancellationToken ct = default);

        /// <summary>清除单个节点的全部输出端口。</summary>
        Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>清除整个工作流的节点输出。</summary>
        Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default);
    }
}
