using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 节点图关系全量对账器（DerivedDataAutoMaintenance / I3 悬空分类兜底）：
/// 扫描基础节点库 ur_nodes 的业务节点（NodeType=node），依其持久化关系状态（<see cref="NodeRecord.RelationsJson"/>）
/// 重推导权威边，经 <see cref="ReferenceGrounding.WriteRelationsAsync"/> 逐节点对账，使 LiteDB 图关系收敛到
/// ur_nodes 真源；并列出图库中无对应业务节点的悬空 node 资源（仅报告，不自动删除，防级联误删）。
/// 跨介质（SQLite ur_nodes ↔ LiteDB graph-log.db）无单库原子性，本对账器即「最终一致」兜底：任意节点修改遗漏的
/// 边同步，均可经 <see cref="ReconcileAllAsync"/> 一次性收敛。
/// </summary>
public sealed class NodeGraphReconciler
{
    private readonly INodeStore _nodes;
    private readonly ReferenceGrounding _grounding;

    public NodeGraphReconciler(INodeStore nodes, ReferenceGrounding grounding)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
    }

    /// <summary>全量对账：以本对账器持有的节点库为源，委托守卫 <see cref="ReferenceGrounding.ReconcileAsync"/>（All）
    /// 单次扫描完成逐业务节点对账，直接返回处理节点数（携带关系状态的业务节点数），不再二次全表遍历（RedundancyElimination）。
    /// 守卫未装配 <see cref="ReferenceGrounding.NodeStore"/> 时兜底注入本对账器节点库，保证能重推导权威边并对账恢复。</summary>
    public Task<int> ReconcileAllAsync(CancellationToken ct = default)
    {
        _grounding.NodeStore ??= _nodes;
        return _grounding.ReconcileAsync(MaintenanceScope.All, ct: ct);
    }

    /// <summary>列出图库中无对应业务节点的悬空 node 资源 Id（I3 悬空分类；仅报告不删除，防级联误删）。
    /// 判定**单一实现**已下沉至 <see cref="ReferenceGrounding.ListDanglingNodeResourcesAsync"/>（A5：差异判据与对账裁决同源）。</summary>
    public Task<IReadOnlyList<string>> ListDanglingGraphNodeResourcesAsync(CancellationToken ct = default)
    {
        // 与 ReconcileAllAsync 同口径：兜底注入本对账器节点库，保证能读到业务节点面。
        _grounding.NodeStore ??= _nodes;
        return _grounding.ListDanglingNodeResourcesAsync(ct);
    }
}
