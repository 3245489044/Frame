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
    private readonly ReferenceGrounding _grounding;

    public NodeGraphReconciler(INodeStore nodes, ReferenceGrounding grounding)
    {
        // nodes 参数保留源兼容（G3 起对账不再读 SQL 节点库，声明权威 = LiteDB ur_relations）。
        _ = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
    }

    /// <summary>全量对账：委托守卫 <see cref="ReferenceGrounding.ReconcileAsync"/>（All，以 ur_relations 为参照），
    /// 直接返回处理节点数，不再二次全表遍历（RedundancyElimination）。</summary>
    public Task<int> ReconcileAllAsync(CancellationToken ct = default)
        => _grounding.ReconcileAsync(MaintenanceScope.All, ct: ct);

    /// <summary>列出图库中无任何引用边的悬空 node 端点 Id（I3 悬空分类；仅报告不删除，防级联误删）。
    /// 判定**单一实现**已下沉至 <see cref="ReferenceGrounding.ListDanglingNodeResourcesAsync"/>。</summary>
    public Task<IReadOnlyList<string>> ListDanglingGraphNodeResourcesAsync(CancellationToken ct = default)
        => _grounding.ListDanglingNodeResourcesAsync(ct);
}
