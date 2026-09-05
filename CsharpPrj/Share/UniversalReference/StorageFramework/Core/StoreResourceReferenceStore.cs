using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>
/// 资源+引用存储（<see cref="IResourceReferenceStore"/> 的单一通用实现）。在 <see cref="IResourceStorage"/> 之上
/// 实现完整端口契约，语义映射经 <see cref="ResourceStoreMapper"/>；各后端只提供薄存储原语。
/// 消除 Sqlite/LiteDB 重复的 CRUD + 映射实现（SingleImplementation，对齐 TH-12）。
/// </summary>
public sealed class StoreResourceReferenceStore : IResourceReferenceStore
{
    private readonly IResourceStorage _storage;

    public StoreResourceReferenceStore(IResourceStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task AddResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default)
    {
        _storage.AddResource(ResourceStoreMapper.ToCreateRecord(resourceId, metadata, symbol));
        return Task.CompletedTask;
    }

    public Task UpdateResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default)
    {
        // 统一 upsert 语义（跨后端一致）：资源存在 → 只更新可更新字段；不存在 → 新增（含 CreatedAt=now）。
        if (!_storage.UpdateResource(ResourceStoreMapper.ToUpdateRecord(resourceId, metadata, symbol)))
            _storage.AddResource(ResourceStoreMapper.ToCreateRecord(resourceId, metadata, symbol));
        return Task.CompletedTask;
    }

    public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
    {
        var record = _storage.GetResource(resourceId.GetUniqueKey());
        return Task.FromResult(record is null ? null : ResourceStoreMapper.ToResource(record));
    }

    public Task<IReadOnlyList<Resource>> GetAllResourcesAsync(CancellationToken ct = default)
    {
        var list = new List<Resource>();
        foreach (var record in _storage.GetAllResources())
            list.Add(ResourceStoreMapper.ToResource(record));
        return Task.FromResult<IReadOnlyList<Resource>>(list);
    }

    public Task RemoveResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
    {
        _storage.DeleteResource(resourceId.GetUniqueKey());
        // 引用完整性：删除资源的同时级联删除其全部入/出引用边（按引用键归一，消除悬空引用）。
        _storage.RemoveReferencesByNode(PersistenceHelper.ResourceRefKey(resourceId));
        return Task.CompletedTask;
    }

    public async Task AddReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, double strength = 1.0, CancellationToken ct = default)
    {
        // 引用完整性 fail-fast：写入引用边前校验端点资源存在（StructuralGrounding），避免悬空引用入图/入库。
        var source = await GetResourceAsync(sourceId, ct).ConfigureAwait(false);
        var target = await GetResourceAsync(targetId, ct).ConfigureAwait(false);
        if (source is null || target is null)
        {
            throw new InvalidOperationException(
                $"引用完整性 fail-fast：端点资源不存在，拒绝写入引用边。source={PersistenceHelper.ResourceRefKey(sourceId)} exists={source is not null}，target={PersistenceHelper.ResourceRefKey(targetId)} exists={target is not null}。请先 AddResource 建立端点。");
        }
        _storage.AddReference(ResourceStoreMapper.ToReferenceRecord(sourceId, targetId, refType, strength));
    }

    public Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, CancellationToken ct = default)
    {
        var record = ResourceStoreMapper.ToReferenceRecord(sourceId, targetId, refType, 0);
        _storage.RemoveReference(record.SourceId, record.TargetId, record.RefType);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true, CancellationToken ct = default)
    {
        var id = PersistenceHelper.ResourceRefKey(resourceId);
        var result = new List<ResourceReference>();
        foreach (var record in _storage.GetReferencesBySource(id))
            result.Add(ResourceStoreMapper.ToResourceReference(record, isIncoming: false));
        if (includeIncoming)
            foreach (var record in _storage.GetReferencesByTarget(id))
                result.Add(ResourceStoreMapper.ToResourceReference(record, isIncoming: true));
        return Task.FromResult<IReadOnlyList<ResourceReference>>(result);
    }
}
