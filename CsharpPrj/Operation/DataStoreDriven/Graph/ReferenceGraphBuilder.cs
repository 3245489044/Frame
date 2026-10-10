using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用库装配期构建器（G6 / D4）：由 USharp 声明（workflows + reference + placement + domains）投影出的边集
/// （逻辑边 + 位置边 + 数据流边 + 控制流边）在**装配期**一次性写入引用库，取代运行期对账重建
/// （<see cref="NodeGraphReconciler"/>）。权威链 = .ushar 声明 → 引用库 → 框架管理；
/// 写边仍经唯一 owner <see cref="ReferenceGraphCoordinator"/>（TH-12）。
/// </summary>
public sealed class ReferenceGraphBuilder
{
    private readonly ReferenceGraphCoordinator _graph;

    public ReferenceGraphBuilder(ReferenceGraphCoordinator graph)
        => _graph = graph ?? throw new System.ArgumentNullException(nameof(graph));

    /// <summary>装配静态边（控制流 Calls / 组合 Owns / 位置 at·contains / 导出·继承·依赖等），幂等。</summary>
    public async Task BuildStaticAsync(IReadOnlyCollection<StaticReference> staticEdges, CancellationToken ct = default)
    {
        if (staticEdges is null || staticEdges.Count == 0) return;
        foreach (var e in staticEdges)
        {
            if (e is null) continue;
            await _graph.AddReferenceAsync(
                new ResourceIdentifier { ResourceType = e.SourceResourceType, Id = e.SourceId },
                new ResourceIdentifier { ResourceType = e.TargetResourceType, Id = e.TargetId },
                e.RefType, e.Strength, ct).ConfigureAwait(false);
        }
    }

    /// <summary>装配节点关系边（工作流 inports/outports 声明 → node_to_node；由 <see cref="NodeRelationDeriver.DeriveDeclaredDataflowEdges"/> 产出）。</summary>
    public async Task BuildRelationsAsync(string workflowId, IReadOnlyCollection<NodeRelation> relations, CancellationToken ct = default)
    {
        if (relations is null || relations.Count == 0) return;
        foreach (var r in relations)
        {
            if (r is null) continue;
            await _graph.AddRelationAsync(workflowId, r, ct).ConfigureAwait(false);
        }
    }

    /// <summary>经**写外盒**装配静态边（G8）：登记 → 冲刷（幂等重放），使装配写具备跨介质补偿能力。
    /// 写仍收敛到唯一 owner <see cref="ReferenceGraphCoordinator"/>（外盒仅承载补偿，不开第二写路径）。</summary>
    public async Task BuildStaticViaOutboxAsync(IReadOnlyCollection<StaticReference> staticEdges,
        IReferenceOutbox outbox, CancellationToken ct = default)
    {
        if (staticEdges is null || staticEdges.Count == 0 || outbox is null) return;
        foreach (var e in staticEdges)
        {
            if (e is null) continue;
            await outbox.EnqueueAsync(e, ct).ConfigureAwait(false);
        }
        await outbox.FlushAsync(ct).ConfigureAwait(false);
    }
}
