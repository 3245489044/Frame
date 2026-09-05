using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>后端无关的资源记录（Sqlite 行 / LiteDB Bson 的映射中间态）。</summary>
public sealed class ResourceRecord
{
    public string Key { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? IdentifierId { get; set; }
    public string? Name { get; set; }
    public string? Symbol { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public string CreatedAt { get; set; } = string.Empty;
    public string LastModified { get; set; } = string.Empty;
}

/// <summary>后端无关的引用记录（Sqlite 行 / LiteDB Bson 的映射中间态）。</summary>
public sealed class ReferenceRecord
{
    public string SourceId { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string RefType { get; set; } = string.Empty;
    public double Strength { get; set; } = 1.0;
}

/// <summary>
/// 资源/引用映射单一权威：<see cref="IResourceReferenceStore"/> 各后端（Sqlite/LiteDB）共享的
/// 语义映射（键归一、metadata 序列化、时间戳、DTO↔<see cref="Resource"/>/<see cref="ResourceReference"/>）。
/// 收敛跨后端重复映射实现，消除 E1/E2 类行为分叉（SingleImplementation，对齐 TH-12）。
/// </summary>
public static class ResourceStoreMapper
{
    /// <summary>新增资源：构造完整记录（键/字段/metadata 序列化/created+modified=now）。</summary>
    public static ResourceRecord ToCreateRecord(ResourceIdentifier id, IReadOnlyDictionary<string, object>? metadata, string? symbol) => new()
    {
        Key = id.GetUniqueKey(),
        ResourceType = id.ResourceType,
        IdentifierId = id.Id,
        Name = id.Name,
        Symbol = symbol,
        MetadataJson = JsonSerializer.Serialize(metadata ?? new Dictionary<string, object>(), PersistenceHelper.JsonOptions),
        CreatedAt = PersistenceHelper.NowIso(),
        LastModified = PersistenceHelper.NowIso(),
    };

    /// <summary>更新资源：仅构造更新相关字段（键/symbol/metadata/LastModified）。</summary>
    public static ResourceRecord ToUpdateRecord(ResourceIdentifier id, IReadOnlyDictionary<string, object>? metadata, string? symbol) => new()
    {
        Key = id.GetUniqueKey(),
        Symbol = symbol,
        MetadataJson = JsonSerializer.Serialize(metadata ?? new Dictionary<string, object>(), PersistenceHelper.JsonOptions),
        LastModified = PersistenceHelper.NowIso(),
    };

    /// <summary>构造引用记录（源/目标引用键经 <see cref="PersistenceHelper.ResourceRefKey"/> 归一）。</summary>
    public static ReferenceRecord ToReferenceRecord(ResourceIdentifier source, ResourceIdentifier target, string refType, double strength) => new()
    {
        SourceId = PersistenceHelper.ResourceRefKey(source),
        SourceType = source.ResourceType,
        TargetId = PersistenceHelper.ResourceRefKey(target),
        TargetType = target.ResourceType,
        RefType = refType,
        Strength = strength,
    };

    /// <summary>记录 → Resource（含 metadata 反序列化与时间戳解析）。</summary>
    public static Resource ToResource(ResourceRecord r)
    {
        var resource = new Resource
        {
            Identifier = new ResourceIdentifier
            {
                ResourceType = r.ResourceType,
                Id = r.IdentifierId,
                Name = r.Name,
            },
            Symbol = r.Symbol,
            Metadata = PersistenceHelper.ParseMetadata(r.MetadataJson),
        };
        if (DateTime.TryParse(r.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
            resource.CreatedAt = created;
        if (DateTime.TryParse(r.LastModified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var modified))
            resource.LastModified = modified;
        return resource;
    }

    /// <summary>记录 → ResourceReference（含入/出方向标记）。</summary>
    public static ResourceReference ToResourceReference(ReferenceRecord r, bool isIncoming) => new()
    {
        Source = new ResourceIdentifier { Id = r.SourceId, ResourceType = r.SourceType },
        Target = new ResourceIdentifier { Id = r.TargetId, ResourceType = r.TargetType },
        RefType = r.RefType,
        Strength = r.Strength,
        IsIncoming = isIncoming,
    };
}
