using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 资源端口实现（A3 拆双角色）—— 只实现 <see cref="IResourceReferenceStore"/>（资源/引用读写），
/// 与 <see cref="GraphRelationStore"/> **共享同一 <see cref="ReferenceGraphCoordinator"/> owner 实例**
/// ⇒ 同一 LiteDatabase 句柄 + 同一 <c>_gate</c> 单写信号量（R5：两端口各持一个门即破单写语义 ⇒ 强制共享）。
/// <para>
/// 零行为差异：全部成员逐字转发至 owner（经接口转型，故不依赖 owner 成员的可见性形态）；
/// <c>AddReferenceAsync</c> 实现仍**恰 1 处**（owner 内），本类不复制任何写边逻辑（TH-12）。
/// 消费面零改动：注册点可择一（本端口 / 兼容的 owner），二者行为等价。
/// </para>
/// </summary>
public sealed class GraphResourceStore : IResourceReferenceStore
{
    private readonly ReferenceGraphCoordinator _owner;

    /// <param name="owner">共享写边 owner（同一实例即同一句柄 + 同一门）。</param>
    public GraphResourceStore(ReferenceGraphCoordinator owner)
        => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>共享的写边 owner（诊断/对账可经此取兼容面）。</summary>
    public ReferenceGraphCoordinator Owner => _owner;

    private IResourceReferenceStore Port => _owner;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken ct = default) => Port.InitializeAsync(ct);

    /// <inheritdoc />
    public Task AddResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null,
        string? symbol = null, CancellationToken ct = default)
        => Port.AddResourceAsync(resourceId, metadata, symbol, ct);

    /// <inheritdoc />
    public Task UpdateResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null,
        string? symbol = null, CancellationToken ct = default)
        => Port.UpdateResourceAsync(resourceId, metadata, symbol, ct);

    /// <inheritdoc />
    public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
        => Port.GetResourceAsync(resourceId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<Resource>> GetAllResourcesAsync(CancellationToken ct = default)
        => Port.GetAllResourcesAsync(ct);

    /// <inheritdoc />
    public Task RemoveResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
        => Port.RemoveResourceAsync(resourceId, ct);

    /// <inheritdoc />
    public Task AddReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType,
        double strength = 1.0, CancellationToken ct = default)
        => Port.AddReferenceAsync(sourceId, targetId, refType, strength, ct);

    /// <inheritdoc />
    public Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType,
        CancellationToken ct = default)
        => Port.RemoveReferenceAsync(sourceId, targetId, refType, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId,
        bool includeIncoming = true, CancellationToken ct = default)
        => Port.GetReferencesAsync(resourceId, includeIncoming, ct);
}

/// <summary>
/// 节点关系端口实现（A3 拆双角色）—— 只实现 <see cref="INodeRelationStore"/>，
/// 与 <see cref="GraphResourceStore"/> **共享同一 owner 实例**（同句柄 + 同一门；不产生第二写边路径）。
/// <para>零行为差异：逐字转发；refType 编解码单源仍为 <see cref="NodeRelationRefCodec"/>。</para>
/// </summary>
public sealed class GraphRelationStore : INodeRelationStore
{
    private readonly ReferenceGraphCoordinator _owner;

    /// <param name="owner">共享写边 owner（同一实例即同一句柄 + 同一门）。</param>
    public GraphRelationStore(ReferenceGraphCoordinator owner)
        => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>共享的写边 owner（诊断/对账可经此取兼容面）。</summary>
    public ReferenceGraphCoordinator Owner => _owner;

    private INodeRelationStore Port => _owner;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken ct = default) => Port.InitializeAsync(ct);

    /// <inheritdoc />
    public Task EnsureNodeAsync(string workflowId, string nodeKey, string? symbol = null,
        IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
        => Port.EnsureNodeAsync(workflowId, nodeKey, symbol, metadata, ct);

    /// <inheritdoc />
    public Task AddRelationAsync(string workflowId, NodeRelation relation, CancellationToken ct = default)
        => Port.AddRelationAsync(workflowId, relation, ct);

    /// <inheritdoc />
    public Task RemoveRelationAsync(string workflowId, NodeRelation relation, CancellationToken ct = default)
        => Port.RemoveRelationAsync(workflowId, relation, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<NodeRelation>> GetRelationsAsync(string workflowId, string nodeKey,
        NodeRelationType? type = null, bool includeIncoming = true, CancellationToken ct = default)
        => Port.GetRelationsAsync(workflowId, nodeKey, type, includeIncoming, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetRelatedNodesAsync(string workflowId, string nodeKey,
        NodeRelationType type, bool outgoing = true, CancellationToken ct = default)
        => Port.GetRelatedNodesAsync(workflowId, nodeKey, type, outgoing, ct);
}

/// <summary>两端口**同源装配点**（A3）：保证两个接口面出自同一 owner（同一句柄 + 同一门），禁各自 new 一个 owner。</summary>
public static class GraphPortStores
{
    /// <summary>以同一 owner 实例构造资源端口与关系端口。</summary>
    /// <param name="owner">共享写边 owner。</param>
    public static (GraphResourceStore Resources, GraphRelationStore Relations) Create(ReferenceGraphCoordinator owner)
        => (new GraphResourceStore(owner), new GraphRelationStore(owner));
}
