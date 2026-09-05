using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.LoopFoldMethod.Reference.Derivation;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.LoopFoldMethod.Reference.Gateways;

/// <summary>
/// 节点/资源引用关系编排门面（Reference 空间维度，中性编排 owner）—— 单一门面（SingleImplementation / TH-12），
/// 直接服务 <c>NodeExecutionService</c>（节点执行期 B 类引用写）与 <c>AgentUiLibraryData</c>（资源/引用 CRUD），
/// 替代已删除的 TaskNodeRelationService / ResourceRelationService 转发门面。只编排，写边原语唯一 owner 留 DataStoreDriven。
/// <para>
/// 职责边界（SingleImplementation / TH-12）：
///   · 推导（<see cref="SerializeRelations"/> / <see cref="DeriveEdgesForNode"/>）：纯推演，委托
///     <see cref="EdgeDeriver"/>（→ NodeRelationDeriver），不复制推导逻辑；
///   · 节点执行期引用写：委托 <see cref="ReferenceGrounding"/>（唯一守卫）的 <c>WriteRelationsAsync</c> /
///     <c>EnsureTaskNodeLinkAsync</c>（写透 store 时跳过，防御性回退不改单层转发语义）；
///   · 资源/引用 CRUD：委托 <see cref="IResourceReferenceStore"/>（唯一写边 owner，图协调器）。
/// </para>
/// <para>
/// 可空装配：只读查询未装配返回空集，写操作未装配抛清晰异常（资源/引用写入口语义）；
/// 节点执行期写未装配守卫 → no-op（节点执行写入口语义）。
/// </para>
/// </summary>
public sealed class NodeRelationGateway
{
    private readonly ReferenceGrounding? _grounding;
    private readonly IResourceReferenceStore? _store;

    /// <param name="grounding">引用守卫（可空；未装配则节点执行期写 no-op）。</param>
    /// <param name="store">引用图读/写侧（可空；未装配则资源查询空集、资源写抛异常）。</param>
    public NodeRelationGateway(ReferenceGrounding? grounding, IResourceReferenceStore? store)
    {
        _grounding = grounding;
        _store = store;
    }

    /// <summary>暴露底层 store（组合根复用 / 测试断言）。</summary>
    public IResourceReferenceStore? Store => _store;

    // ---- 推导（纯推演，委托 EdgeDeriver → NodeRelationDeriver）----

    /// <summary>把「映射存在性 + 溯源」序列化为节点关系状态 JSON（持久化到 NodeRecord.RelationsJson）。</summary>
    public static string SerializeRelations(
        bool hasMapping, IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
        => EdgeDeriver.SerializeRelations(hasMapping, provenance);

    /// <summary>推导某节点的权威节点关系边全集（KeyMappingToNode + NodeToNode），供统一写入/对账。</summary>
    public static IReadOnlyList<NodeRelation> DeriveEdgesForNode(
        string workflowId, string nodeKey, bool hasMapping,
        IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
        => EdgeDeriver.DeriveEdgesForNode(workflowId, nodeKey, hasMapping, provenance);

    // ---- 节点执行期引用写编排 ----

    /// <summary>经守卫写权威边集（唯一守卫端口）。未装配守卫 → no-op；
    /// 节点库为写透守卫（WriteGuardPort）时由 PutAsync 自动完成图镜像 + 权威边推导，仅非守卫时显式写。</summary>
    public Task WriteRelationsAsync(
        string workflowId, string nodeKey,
        IReadOnlyList<NodeRelation> edges,
        bool isWriteThroughStore, CancellationToken ct)
    {
        if (_grounding is null || isWriteThroughStore) return Task.CompletedTask;
        return _grounding.WriteRelationsAsync(workflowId, nodeKey, edges, ct);
    }

    /// <summary>运行时 task 归属边（cross_db_link）：task（ur_tasks）→ 本节点（ur_nodes）。
    /// 未装配守卫 / taskKey 空 → no-op。</summary>
    public Task EnsureTaskNodeLinkAsync(string? taskKey, string workflowId, string nodeKey, CancellationToken ct)
    {
        if (_grounding is null || string.IsNullOrWhiteSpace(taskKey)) return Task.CompletedTask;
        return _grounding.EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);
    }

    // ---- 资源/引用 CRUD 编排 ----

    /// <summary>查询单个资源；未装配 store → null。</summary>
    public Task<Resource?> GetResourceAsync(string resourceType, string id, CancellationToken ct = default)
        => _store is null ? Task.FromResult<Resource?>(null)
            : _store.GetResourceAsync(Id(resourceType, id), ct);

    /// <summary>列出全部资源节点；未装配 store → 空集。</summary>
    public Task<IReadOnlyList<Resource>> ListResourcesAsync(CancellationToken ct = default)
        => _store is null ? Task.FromResult<IReadOnlyList<Resource>>(Array.Empty<Resource>())
            : _store.GetAllResourcesAsync(ct);

    /// <summary>幂等新增资源节点（写操作；未装配 store → 抛异常）。</summary>
    public Task AddResourceAsync(string resourceType, string id, string? name = null, CancellationToken ct = default)
        => RequireStore().AddResourceAsync(Id(resourceType, id, name), metadata: null, symbol: null, ct);

    /// <summary>删除资源节点（级联清其引用边；写操作；未装配 store → 抛异常）。</summary>
    public Task RemoveResourceAsync(string resourceType, string id, CancellationToken ct = default)
        => RequireStore().RemoveResourceAsync(Id(resourceType, id), ct);

    /// <summary>幂等新增引用边（先删后加 upsert；写操作；未装配 store → 抛异常）。</summary>
    public Task AddReferenceAsync(string sourceType, string sourceId, string targetType, string targetId, string refType, CancellationToken ct = default)
        => RequireStore().AddReferenceAsync(Id(sourceType, sourceId), Id(targetType, targetId), refType, strength: 1.0, ct);

    /// <summary>删除引用边（写操作；未装配 store → 抛异常）。</summary>
    public Task RemoveReferenceAsync(string sourceType, string sourceId, string targetType, string targetId, string refType, CancellationToken ct = default)
        => RequireStore().RemoveReferenceAsync(Id(sourceType, sourceId), Id(targetType, targetId), refType, ct);

    /// <summary>列出某资源全部引用关系（含入/出边）；未装配 store → 空集。</summary>
    public Task<IReadOnlyList<ResourceReference>> ListReferencesAsync(string resourceType, string id, bool includeIncoming = true, CancellationToken ct = default)
        => _store is null ? Task.FromResult<IReadOnlyList<ResourceReference>>(Array.Empty<ResourceReference>())
            : _store.GetReferencesAsync(Id(resourceType, id), includeIncoming, ct);

    private static ResourceIdentifier Id(string resourceType, string id, string? name = null)
        => new() { ResourceType = resourceType, Id = id, Name = name };

    private IResourceReferenceStore RequireStore()
        => _store ?? throw new InvalidOperationException("资源引用库（IResourceReferenceStore）未装配：资源/引用写操作不可用。");
}
