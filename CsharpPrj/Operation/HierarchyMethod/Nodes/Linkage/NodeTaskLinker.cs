using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// task→node 边自动建链端口（节点+边为基础，LLM-free）。
/// 幂等地把「任务产出的节点」挂到任务上：确保 task/节点图端点镜像 + 建 <c>cross_db_link</c> 边
/// （委托 <see cref="IReferenceMaintenance.EnsureTaskNodeLinkAsync"/>，唯一守卫 → 图协调器纯写边，SingleImplementation / TH-12）。
/// 一切以节点和边为基础：节点键归一化（裸 / {wf}.{node} / nodes.{wf}.{node} 三形态）、workflowId 空串归一化。
/// 消费方（会话管线 / 任务池执行器）在任务运行后以任务作用域的本轮产出节点键调用本端口，为普通流程自动建边。
/// </summary>
public interface INodeTaskLinker
{
    /// <summary>幂等建链：task → (workflowId, nodeKey) cross_db_link 边。返回成功建链节点数（去重后）。</summary>
    Task<int> LinkTaskNodesAsync(
        string taskKey, string workflowId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct = default);
}

/// <summary>
/// 默认实现（SingleImplementation / TH-12）：只编排（何时建/哪些键/镜像次序），写边原语委托
/// <see cref="IReferenceMaintenance"/>（唯一守卫 → <c>ReferenceGraphCoordinator</c> 纯写边），不复制任何写边逻辑。
/// </summary>
public sealed class NodeTaskLinker : INodeTaskLinker
{
    private readonly IReferenceMaintenance _maintenance;

    /// <param name="maintenance">引用维护守卫（实现 <see cref="IReferenceMaintenance"/>，如 ReferenceGrounding）。</param>
    public NodeTaskLinker(IReferenceMaintenance maintenance)
        => _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));

    public async Task<int> LinkTaskNodesAsync(
        string taskKey, string workflowId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return 0;
        if (nodeKeys is null || nodeKeys.Count == 0) return 0;

        var wf = NodeOutputKeys.Normalize(workflowId);
        var keys = nodeKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => Bare(k!))
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0) return 0;

        // task 端点镜像（幂等）：先于边，保证图有源端点。
        await _maintenance.EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);

        foreach (var key in keys)
        {
            // 节点端点镜像（幂等）：节点即存储单元，先建端点再建边。
            await _maintenance.EnsureNodeMirrorAsync(wf, key, null, ct).ConfigureAwait(false);
            // cross_db_link 边（真多对多，幂等）：task → node 运行时产出归属。
            await _maintenance.EnsureTaskNodeLinkAsync(taskKey, wf, key, ct).ConfigureAwait(false);
        }

        return keys.Count;
    }

    /// <summary>节点键归一化为裸 nodeKey：剥离 <c>nodes.</c> 与 <c>{wf}.</c> 前缀。</summary>
    private static string Bare(string nodeKey)
    {
        if (nodeKey.StartsWith("nodes.", StringComparison.Ordinal))
            nodeKey = nodeKey.Substring("nodes.".Length);
        var dot = nodeKey.IndexOf('.');
        return dot > 0 ? nodeKey.Substring(dot + 1) : nodeKey;
    }
}
