using UniversalWorkflowFramework.Relations.Kernel;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Derivation;

/// <summary>
/// 规格化查询（F4）：<see cref="RelationQuerySpec"/> → 有界逐跳键集。
/// <para>
/// 语义：① 路径 = <c>Via</c>（缺省取 <c>Label</c> 单跳）；② 每跳只读该端点出边，按 <c>RefType</c> 过滤；
/// ③ <c>MaxDepth</c> 限跳数、<c>MaxNodes</c> 限每跳节点数（0 = 框架预算），触顶即截断并写入空因位
/// （<c>truncated:max-nodes=N</c>，禁静默）；④ 未声明边/未给起点 ⇒ 空集 + 空因；⑤ 输出按声明归一/排序。
/// </para>
/// </summary>
public sealed partial class RelationDerivationEngine
{
    /// <summary>框架默认节点预算（每跳；声明面未给 <c>MaxNodes</c> 时生效）。</summary>
    public const int DefaultMaxNodes = 1024;

    /// <inheritdoc />
    public async Task<RelationQueryResult> QueryAsync(
        RelationQuerySpec spec, RelationScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(spec.From))
            return RelationQueryResults.Empty("query-invalid:from");

        var via = ResolveVia(spec);
        if (via.Count == 0)
            return RelationQueryResults.Empty("query-undeclared:label");

        var edges = new List<RelationEdgeSpec>(via.Count);
        foreach (var edgeName in via)
        {
            var edge = _policy.FindEdge(edgeName);
            if (edge is null) return RelationQueryResults.Empty("edge-undeclared:" + edgeName);
            edges.Add(edge);
        }

        var maxDepth = spec.MaxDepth <= 0 ? edges.Count : Math.Min(spec.MaxDepth, edges.Count);
        var budget = spec.MaxNodes > 0 ? spec.MaxNodes : DefaultMaxNodes;

        var currentType = string.IsNullOrWhiteSpace(scope.SourceType) ? edges[0].Source : scope.SourceType!;
        var level = new List<string> { spec.From.Trim() };
        var reasons = new List<string>();
        var truncated = false;

        for (var hop = 0; hop < maxDepth && !truncated; hop++)
        {
            var edge = edges[hop];
            var next = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in level)
            {
                var source = RelationEndpoints.Create(currentType, key, LocationFor(currentType, scope));
                var refs = await _reader.GetReferencesAsync(source, includeIncoming: false, ct).ConfigureAwait(false);
                foreach (var reference in refs)
                {
                    if (reference.IsIncoming) continue;
                    if (!string.Equals(reference.RefType, edge.Name, StringComparison.Ordinal)) continue;

                    var id = reference.Target.Id ?? string.Empty;
                    if (id.Length == 0) continue;
                    if (!seen.Add(id)) continue;

                    if (next.Count >= budget)
                    {
                        truncated = true;
                        break;
                    }

                    next.Add(id);
                }

                if (truncated) break;
            }

            level = next;
            currentType = string.IsNullOrWhiteSpace(edge.Target) ? currentType : edge.Target;

            if (level.Count == 0) return RelationQueryResults.Empty("no-member");
        }

        if (truncated) reasons.Add($"truncated:max-nodes={budget}");

        var keys = level;
        if (string.Equals(spec.Normalize, RelationQueryVocabulary.NormalizeNodeKey, StringComparison.Ordinal))
            keys = keys.Select(NormalizeNodeKey).Distinct(StringComparer.Ordinal).ToList();

        if (!string.IsNullOrWhiteSpace(spec.To))
        {
            var wanted = spec.To.Trim();
            keys = keys.Where(k => string.Equals(k, wanted, StringComparison.Ordinal)).ToList();
        }

        if (string.Equals(spec.Order, RelationQueryVocabulary.OrderKeyOrdinal, StringComparison.Ordinal))
            keys = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        return keys.Count == 0 && reasons.Count == 0
            ? RelationQueryResults.Empty("no-member")
            : new RelationQueryResult(keys, reasons);
    }

    private static IReadOnlyList<string> ResolveVia(RelationQuerySpec spec)
    {
        if (spec.Via is { Count: > 0 }) return spec.Via;
        return string.IsNullOrWhiteSpace(spec.Label) ? Array.Empty<string>() : new[] { spec.Label.Trim() };
    }
}
