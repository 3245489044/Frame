using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 节点图关系全量对账器（G3 起 LiteDB 自洽兜底）：
/// 委托守卫 <see cref="ReferenceGrounding.ReconcileAsync"/> 以 LiteDB 声明集合（ur_relations）为参照对账，
/// 使 LiteDB 图关系自洽收敛；并列出图库中无任何引用边的悬空 node 端点（仅报告，不自动删除，防级联误删）。
/// </summary>
public sealed class NodeGraphReconciler
{
    private readonly IGraphGrounding _grounding;
    private readonly INodeStore? _nodes;

    public NodeGraphReconciler(INodeStore nodes, IGraphGrounding grounding)
    {
        _nodes = nodes;
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
    }

    /// <summary>全量对账：优先按节点库逐节点重推导权威边（持久化关系状态 → 图）；未装配节点库 ⇒ 声明集对账。</summary>
    public async Task<int> ReconcileAllAsync(CancellationToken ct = default)
    {
        if (_nodes is null)
            return await _grounding.ReconcileAsync(MaintenanceScope.All, ct: ct).ConfigureAwait(false);

        var nodes = await _nodes.ListAsync(NodeTypes.Node, null, null, 100000, ct).ConfigureAwait(false);
        var processed = 0;
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var workflowId = ConfiguredDefaultWorkflowIdProvider.Default.Resolve(node.ThreadId);
            var edges = _grounding.AuthoritativeEdgesFor(workflowId, node);
            await _grounding.WriteRelationsAsync(workflowId, node.NodeKey, edges, ct).ConfigureAwait(false);
            processed++;
        }
        return processed;
    }

    /// <summary>列出图库中无任何引用边的悬空 node 端点 Id（I3 悬空分类；仅报告不删除，防级联误删）。
    /// 判定**单一实现**已下沉至 <see cref="ReferenceGrounding.ListDanglingNodeResourcesAsync"/>。</summary>
    public Task<IReadOnlyList<string>> ListDanglingGraphNodeResourcesAsync(CancellationToken ct = default)
        => _grounding.ListDanglingNodeResourcesAsync(ct);
}
