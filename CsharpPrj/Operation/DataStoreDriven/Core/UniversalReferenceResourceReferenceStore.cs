using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.ReferenceSchema;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 把 <see cref="IUniversalReferenceManager"/>（UniversalReference 底层）适配为 <see cref="IResourceReferenceStore"/> 端口。
/// 供 Graph / Local 后端复用：业务只依赖端口，底层仍由 UniversalReference 提供。
/// 当注入 <see cref="IReferenceTypeRegistry"/>（编译自引用 Schema DSL）时，引用类型在写入前 fail-fast 校验：
/// 未声明 / 已废弃的类型直接拒绝，替代把未知类型静默降级为 <c>ReferenceType.Other</c> 的旧行为。
/// </summary>
public sealed class UniversalReferenceResourceReferenceStore : IResourceReferenceStore
{
    private readonly IUniversalReferenceManager _manager;
    private readonly IReferenceTypeRegistry? _registry;

    public UniversalReferenceResourceReferenceStore(IUniversalReferenceManager manager, IReferenceTypeRegistry? registry = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _registry = registry;
    }

    public Task InitializeAsync(CancellationToken ct = default)
        => _manager.InitializeAsync();

    public Task AddResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default)
        => _manager.AddResourceAsync(resourceId, metadata is null ? null : new Dictionary<string, object>(metadata), symbol);

    public async Task UpdateResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null, string? symbol = null, CancellationToken ct = default)
    {
        // 契约统一（对齐 StoreResourceReferenceStore 的 upsert 语义，消除 Sqlite/LiteDB 与 Local/Graph 后端行为分叉，对齐 StateSnapshot 确定性）：
        // 资源不存在 → 新增；已存在 → 仅更新。
        ct.ThrowIfCancellationRequested();
        var existing = await _manager.GetResourceAsync(resourceId).ConfigureAwait(false);
        var payload = metadata is null ? null : new Dictionary<string, object>(metadata);
        if (existing is null)
            await _manager.AddResourceAsync(resourceId, payload, symbol).ConfigureAwait(false);
        else
            await _manager.UpdateResourceAsync(resourceId, payload, symbol).ConfigureAwait(false);
    }

    public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
        => _manager.GetResourceAsync(resourceId);

    public async Task<IReadOnlyList<Resource>> GetAllResourcesAsync(CancellationToken ct = default)
        => await _manager.GetAllResourcesAsync().ConfigureAwait(false);

    public Task RemoveResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
        => _manager.RemoveResourceAsync(resourceId);

    public Task AddReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, double strength = 1.0, CancellationToken ct = default)
        => _manager.AddReferenceAsync(sourceId, targetId, ToReferenceType(refType), strength: strength);

    public Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType, CancellationToken ct = default)
        => _manager.RemoveReferenceAsync(sourceId, targetId, ToReferenceType(refType));

    public async Task<IReadOnlyList<UniversalWorkflowFramework.StorageFramework.Abstractions.ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true, CancellationToken ct = default)
    {
        var result = new List<UniversalWorkflowFramework.StorageFramework.Abstractions.ResourceReference>();
        var refs = await _manager.GetReferencesAsync(resourceId, includeIncoming).ConfigureAwait(false);
        foreach (var (source, target, type, _, _, strength, isIncoming) in refs)
        {
            result.Add(new UniversalWorkflowFramework.StorageFramework.Abstractions.ResourceReference
            {
                Source = source,
                Target = target,
                RefType = type.ToString(),
                Strength = strength,
                IsIncoming = isIncoming,
            });
        }
        return result;
    }

    private ReferenceType ToReferenceType(string refType)
    {
        if (_registry is not null)
        {
            // fail-fast：引用类型须已在 Schema DSL 词汇表声明，且未废弃；否则拒绝（替代静默降级 Other）
            if (!_registry.IsDeclared(refType))
                throw new InvalidOperationException(
                    $"未声明的引用类型 '{refType}'（须在 reference-schema.dsl.md 的 [Edge] 词汇表中声明）。");
            if (_registry.IsDeprecated(refType))
                throw new InvalidOperationException(
                    $"引用类型 '{refType}' 已废弃（dep），禁止新增引用。");
            // 已声明但未映射到 ReferenceType 枚举的边同样拒绝，而非静默降级 Other（与 fail-fast 初衷一致）。
            if (!Enum.TryParse<ReferenceType>(refType, ignoreCase: true, out var declaredParsed))
                throw new InvalidOperationException(
                    $"已声明的引用类型 '{refType}' 未映射到 ReferenceType 枚举（须在 Schema 词汇表与枚举间对齐）。");
            return declaredParsed;
        }
        // 无 registry（未启用引用 Schema 校验的后端）时保留旧回退语义。
        return Enum.TryParse<ReferenceType>(refType, ignoreCase: true, out var parsed) ? parsed : ReferenceType.Other;
    }
}
