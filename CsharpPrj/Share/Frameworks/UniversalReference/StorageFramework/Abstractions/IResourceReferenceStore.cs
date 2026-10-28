using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>
/// 资源 + 引用关系存储端口。数据模型复用 UniversalReference 的 <see cref="Resource"/> /
/// <see cref="ResourceIdentifier"/>（Operation.DataStoreDriven 依赖 UniversalReference 底层基础设施）。
/// </summary>
public interface IResourceReferenceStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task AddResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default);

    Task UpdateResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default);

    Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default);

    Task<IReadOnlyList<Resource>> GetAllResourcesAsync(CancellationToken ct = default);

    Task RemoveResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default);

    Task AddReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, double strength = 1.0, CancellationToken ct = default);

    /// <summary>
    /// 写边（带写边角色 + 结构化裁决）；relation 内核的唯一写面。
    /// <para><paramref name="role"/> 为开放受控词（business / reconcile / declare）；
    /// 实现具备写前门时返回 Written/Skipped/Denied，不具备时返回 <c>WrittenWithoutPolicy</c>（显式降级，禁伪装）。</para>
    /// </summary>
    Task<ReferenceWriteOutcome> AddReferenceResultAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, string role, double strength = 1.0, CancellationToken ct = default);

    Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, CancellationToken ct = default);

    /// <summary>删边（带写边角色 + 结构化裁决；未命中必须显式返回 NotFound，禁静默成功）。</summary>
    Task<ReferenceDeleteOutcome> RemoveReferenceResultAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, string role, CancellationToken ct = default);


    Task<IReadOnlyList<ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true, CancellationToken ct = default);
}

/// <summary>一条引用关系记录。</summary>
public sealed class ResourceReference
{
    public ResourceIdentifier Source { get; set; } = new();
    public ResourceIdentifier Target { get; set; } = new();
    public string RefType { get; set; } = string.Empty;
    public double Strength { get; set; } = 1.0;
    public bool IsIncoming { get; set; }
}
