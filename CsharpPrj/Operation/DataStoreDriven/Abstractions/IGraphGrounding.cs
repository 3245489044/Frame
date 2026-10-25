using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 通用图接地端口（IGraphGrounding）：**无任何领域词汇**的图维护面
/// （节点镜像 / 权威边 / 声明 / 对账 / 重建 / 墓碑 / seed）。
/// <para>
/// 域语义（task / history / session）在 <see cref="IReferenceMaintenance"/> 扩展；
/// 核心消费方只依赖本接口，域实现由 <c>Domains</c> 提供（控制反转）。
/// </para>
/// </summary>
public interface IGraphGrounding
{
    /// <summary>写透目标图协调器（graph-log.db 唯一 owner）。</summary>
    ReferenceGraphCoordinator Coordinator { get; }

    /// <summary>兼容读面：暴露为 <see cref="IResourceReferenceStore"/>。</summary>
    IResourceReferenceStore Graph { get; }

    /// <summary>可选准入诊断出口（缺省静默）。</summary>
    IReferenceEdgeDiagnostics? EdgeDiagnostics { get; set; }

    /// <summary>可选归属边白名单（未装配 ⇒ 报告模式放行）。</summary>
    IReferenceEdgeCatalog EdgeCatalog { get; set; }

    /// <summary>可选声明引用源（缺省 ⇒ 回退 RelationsJson 兼容读）。</summary>
    IDeclaredReferenceSource? DeclarationSource { get; set; }

    /// <summary>可选节点库探针（装配后 cross_db_link 写边前断言节点行存在）。</summary>
    global::Operation.DataStoreDriven.Abstractions.INodeStore? NodeStore { get; set; }

    /// <summary>取某节点的权威边（声明源 + LiteDB 声明投影）。</summary>
    IReadOnlyList<NodeRelation> AuthoritativeEdgesFor(string workflowId, NodeRecord node);

    /// <summary>镜像业务节点内容（upsert：更新 symbol/metadata，不删边）。</summary>
    Task MirrorNodeAsync(string workflowId, NodeRecord node, CancellationToken ct = default);

    /// <summary>镜像 node 端点（wf.node）。</summary>
    Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default);

    /// <summary>移除 node 端点镜像（级联清边）。</summary>
    Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default);

    /// <summary>权威边集写入（删残留声明 + 补缺失声明）。</summary>
    Task WriteRelationsAsync(string workflowId, string nodeKey,
        IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default);

    /// <summary>工作流静态引用 seed（tombstone-aware create-only）。</summary>
    Task WriteStaticReferencesAsync(string workflowId,
        IReadOnlyCollection<StaticReference> references, CancellationToken ct = default);

    /// <summary>主动声明关系（清墓碑 + upsert + 建边）。</summary>
    Task DeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<RelationDeclarationEntry> declarations, CancellationToken ct = default);

    /// <summary>撤销声明（物理删 + 墓碑）。</summary>
    Task UndeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<string> relationIds, CancellationToken ct = default);

    /// <summary>列出全部声明。</summary>
    Task<IReadOnlyList<RelationDeclarationEntry>> ListRelationsAsync(string? workflowId = null, CancellationToken ct = default);

    /// <summary>对账（单节点 / 全图）。</summary>
    Task<int> ReconcileAsync(MaintenanceScope scope, string? workflowId = null, string? nodeKey = null, CancellationToken ct = default);

    /// <summary>全量重建（清空图库 → 从声明集合重灌）。</summary>
    Task RebuildAllAsync(CancellationToken ct = default);

    /// <summary>图对账差异报告。</summary>
    Task<GraphMaintenanceDiff> GetDiffAsync(CancellationToken ct = default);

    /// <summary>列悬空 node 端点（仅报告）。</summary>
    Task<IReadOnlyList<string>> ListDanglingNodeResourcesAsync(CancellationToken ct = default);

    /// <summary>装配期声明 seed。</summary>
    Task<int> SeedAsync(CancellationToken ct = default);

    /// <summary>清墓碑 + 重 seed。</summary>
    Task<GraphResyncResult> ResyncAsync(CancellationToken ct = default);

    /// <summary>列墓碑。</summary>
    Task<IReadOnlyList<GraphTombstoneDoc>> ListTombstonesAsync(string? kind = null, CancellationToken ct = default);

    /// <summary>清墓碑。</summary>
    Task<int> ClearTombstonesAsync(string? kind = null, CancellationToken ct = default);

    /// <summary>声明 seed（含 node 数据流）。</summary>
    Task SeedDeclarationsFromAsync(IDeclaredReferenceSource source,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? nodeKeysByWorkflow = null,
        CancellationToken ct = default);
}
