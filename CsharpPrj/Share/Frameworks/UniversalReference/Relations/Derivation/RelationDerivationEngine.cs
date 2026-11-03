using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 派生成员集引擎：<b>唯一读入口</b>（<see cref="IRelationQueryPort"/>）。
/// <para>只做「路径遍历 / 去重 / 归一 / 排序」，不解释策略；每一跳走 <see cref="IResourceReferenceStore.GetReferencesAsync"/>。</para>
/// </summary>
public sealed partial class RelationDerivationEngine : IRelationQueryPort, IRelationSpecQueryPort
{
    private readonly IResourceReferenceStore _reader;
    private readonly RelationPlanCatalog _catalog;
    private readonly IRelationPolicy _policy;
    private readonly IWorkflowNamespace _namespaces;

    public RelationDerivationEngine(
        IResourceReferenceStore reader,
        RelationPlanCatalog catalog,
        IRelationPolicy policy,
        IWorkflowNamespace namespaces)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
    }

    public async Task<RelationQueryResult> EnumerateAsync(
        string edgeOrDerive, RelationScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (_catalog.ResolveDerivation(edgeOrDerive) is not null)
            return await DeriveAsync(edgeOrDerive, scope, ct).ConfigureAwait(false);

        var edge = _policy.FindEdge(edgeOrDerive);
        if (edge is null)
            return RelationQueryResult.Empty("edge-undeclared:" + edgeOrDerive);

        var sourceType = scope.SourceType ?? (string.IsNullOrWhiteSpace(edge.Source) ? "node" : edge.Source);
        var source = RelationEndpoints.Create(sourceType, scope.SourceKey, LocationFor(sourceType, scope));
        var refs = await _reader.GetReferencesAsync(source, includeIncoming: false, ct).ConfigureAwait(false);

        var keys = refs
            .Where(r => !r.IsIncoming && string.Equals(r.RefType, edgeOrDerive, StringComparison.Ordinal))
            .Select(r => r.Target.Id ?? string.Empty)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return keys.Count == 0
            ? RelationQueryResult.Empty("no-member")
            : new RelationQueryResult(keys, Array.Empty<string>());
    }

    public async Task<RelationQueryResult> DeriveAsync(
        string deriveId, RelationScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var derivation = _catalog.ResolveDerivation(deriveId);
        if (derivation is null)
            return RelationQueryResult.Empty("derive-undeclared");

        var currentType = derivation.From;
        var level = new List<string> { scope.SourceKey };

        foreach (var edgeName in derivation.Via)
        {
            var edge = _policy.FindEdge(edgeName);
            if (edge is null)
                return RelationQueryResult.Empty("edge-undeclared:" + edgeName);

            var next = new List<string>();
            foreach (var key in level)
            {
                var source = RelationEndpoints.Create(currentType, key, LocationFor(currentType, scope));
                var refs = await _reader.GetReferencesAsync(source, includeIncoming: false, ct);
                foreach (var reference in refs)
                {
                    if (reference.IsIncoming) continue;
                    if (!string.Equals(reference.RefType, edgeName, StringComparison.Ordinal)) continue;
                    var id = reference.Target.Id ?? string.Empty;
                    if (id.Length > 0) next.Add(id);
                }
            }

            level = next.Distinct(StringComparer.Ordinal).ToList();
            currentType = string.IsNullOrWhiteSpace(edge.Target) ? currentType : edge.Target;

            if (level.Count == 0)
                return RelationQueryResult.Empty(derivation.Empty.Count > 0 ? derivation.Empty[0] : "no-edge");
        }

        var keys = derivation.Normalize switch
        {
            "node-key" => level.Select(NormalizeNodeKey).Distinct(StringComparer.Ordinal).ToList(),
            _ => level,
        };

        if (string.Equals(derivation.Order, "key-ordinal", StringComparison.Ordinal))
            keys = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        return keys.Count == 0
            ? RelationQueryResult.Empty(derivation.Empty.Count > 0 ? derivation.Empty[0] : "no-member")
            : new RelationQueryResult(keys, Array.Empty<string>());
    }

    private string? LocationFor(string entityType, RelationScope scope)
        => string.Equals(entityType, RelationEndpoints.NodeType, StringComparison.Ordinal)
            ? scope.Namespace ?? _namespaces.Default
            : null;

    /// <summary>节点键归一：剥 <c>node:</c> 前缀与 <c>@location</c> 后缀（幂等）。</summary>
    private static string NormalizeNodeKey(string key)
    {
        var value = key;
        var at = value.IndexOf('@');
        if (at >= 0) value = value[..at];
        var colon = value.IndexOf(':');
        if (colon >= 0) value = value[(colon + 1)..];
        return value.Trim();
    }
}
