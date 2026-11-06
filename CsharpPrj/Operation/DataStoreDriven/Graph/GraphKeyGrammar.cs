using System;
using Modules.Node.Relations;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Graph;

/// <summary>端点键反解结果（<see cref="Location"/> 可空 = 旧形态 / 无作用域）。</summary>
public readonly record struct GraphEndpointKey(string ResourceType, string Id, string? Location);

/// <summary>边键反解结果（<see cref="Location"/> 可空 = <c>identity=source_target_ref</c>）。</summary>
public readonly record struct GraphEdgeKey(string SourceKey, string TargetKey, string RefType, string? Location);

/// <summary>
/// 图端点/边键文法端口（K-G）：唯一键构造与反解点。
/// <para>端点键 = <c>{resourceType}:{id}</c>；<c>Location</c> 非空时 = <c>{resourceType}:{id}@{location}</c>（方案 B）。
/// 边键 = <c>{sourceKey}::{targetKey}::{refType}</c>；<c>identity=source_target_ref_location</c> 时追加 <c>@{location}</c>。
/// 旧分隔符 <c>U+0001</c> 仅兼容读取（禁新写）。</para>
/// <para>纪律：全仓只允许本文件手写这些分隔符字面量（门禁 GraphKeySingleSource）。</para>
/// </summary>
public interface IGraphKeyGrammar
{
    /// <summary>端点键（<c>Location</c> 进键）。</summary>
    string Endpoint(ResourceIdentifier id);

    /// <summary>端点键（分字段构造）。</summary>
    string Endpoint(string resourceType, string id, string? location);

    /// <summary>反解端点键（兼容旧形态：无 <c>@location</c>）。</summary>
    bool TryParseEndpoint(string? key, out GraphEndpointKey endpoint);

    /// <summary>边键（按声明身份选择是否含 location）。</summary>
    string Edge(string sourceKey, string targetKey, string refType, EdgeIdentity identity, string? location = null);

    /// <summary>反解边键（<c>Storage</c> / <c>Legacy</c> 两形态）。</summary>
    bool TryParseEdge(string? edgeId, out GraphEdgeKey edge);

    /// <summary>是否旧分隔符形态（只读兼容 / 迁移判据）。</summary>
    bool IsLegacyEdge(string? edgeId);

    /// <summary>以某端点为源的边键前缀（<c>{endpointKey}::</c>）。</summary>
    string EdgePrefix(string endpointKey);

    /// <summary>边键是否以某端点开头（端点级联清边判据）。</summary>
    bool EdgeHasEndpointPrefix(string? edgeId, string endpointKey);
}

/// <summary>
/// 声明驱动的键文法（<c>[KeySpace] graph.*</c> → 运行时）：<see cref="DeclaredGraphKeyGrammar"/> 为唯一实现。
/// </summary>
public sealed class DeclaredGraphKeyGrammar : IGraphKeyGrammar
{
    /// <summary>共享实例（键文法无状态）。</summary>
    public static DeclaredGraphKeyGrammar Instance { get; } = new();

    // 端点拼法/反解/剥位置统一委托 Modules.Node.Relations.EndpointKeyGrammar（B-2 单源）；
    // 本类只保留边键（::）与旧分隔符（U+0001）的存储域语法。
    private const char LocationSeparator = EndpointKeyGrammar.LocationSeparator;
    private const string EdgeSeparator = "::";
    private const char LegacyEdgeSeparator = '\u0001';

    /// <inheritdoc />
    public string Endpoint(ResourceIdentifier id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Endpoint(id.ResourceType ?? string.Empty, id.Id ?? string.Empty, id.Location);
    }

    /// <inheritdoc />
    public string Endpoint(string resourceType, string id, string? location)
        => EndpointKeyGrammar.Compose(resourceType, id, location);

    /// <inheritdoc />
    public bool TryParseEndpoint(string? key, out GraphEndpointKey endpoint)
    {
        if (!EndpointKeyGrammar.TryParse(key, out var resourceType, out var id, out var location))
        {
            endpoint = default;
            return false;
        }

        endpoint = new GraphEndpointKey(resourceType, id, location);
        return true;
    }

    /// <inheritdoc />
    public string Edge(string sourceKey, string targetKey, string refType, EdgeIdentity identity, string? location = null)
    {
        var edge = sourceKey + EdgeSeparator + targetKey + EdgeSeparator + refType;
        return identity == EdgeIdentity.SourceTargetRefLocation && !string.IsNullOrWhiteSpace(location)
            ? edge + LocationSeparator + location
            : edge;
    }

    /// <inheritdoc />
    public bool TryParseEdge(string? edgeId, out GraphEdgeKey edge)
    {
        edge = default;
        if (string.IsNullOrWhiteSpace(edgeId)) return false;

        var parts = edgeId.Split(EdgeSeparator, StringSplitOptions.None);
        if (parts.Length != 3) parts = edgeId.Split(LegacyEdgeSeparator, StringSplitOptions.None);
        if (parts.Length != 3) return false;
        if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
            return false;

        var refType = parts[2];
        string? location = null;
        var at = refType.LastIndexOf(LocationSeparator);
        if (at > 0)
        {
            location = refType[(at + 1)..];
            refType = refType[..at];
            if (location.Length == 0 || refType.Length == 0) return false;
        }

        edge = new GraphEdgeKey(parts[0], parts[1], refType, location);
        return true;
    }

    /// <inheritdoc />
    public bool IsLegacyEdge(string? edgeId)
        => !string.IsNullOrEmpty(edgeId) && edgeId.Contains(LegacyEdgeSeparator);

    /// <inheritdoc />
    public string EdgePrefix(string endpointKey) => endpointKey + EdgeSeparator;

    /// <inheritdoc />
    public bool EdgeHasEndpointPrefix(string? edgeId, string endpointKey)
        => !string.IsNullOrEmpty(edgeId) && edgeId.StartsWith(EdgePrefix(endpointKey), StringComparison.Ordinal);
}
