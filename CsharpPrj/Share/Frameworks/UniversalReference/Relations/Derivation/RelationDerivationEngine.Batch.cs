using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Relations.Kernel;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Derivation;

/// <summary>
/// 派生引擎批量面（F3 扩展）：同一 <c>[Derive]</c> 声明、同一跳序，逐跳一次图枚举即完成全部作用域求值。
/// <para>与单条 <see cref="DeriveAsync"/> 逐字同语义（归一 / 排序 / 空因序一致）；存储不具备批量能力时退化为逐点读（成本上升，语义不变）。</para>
/// </summary>
public sealed partial class RelationDerivationEngine : IRelationBatchQueryPort
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, RelationQueryResult>> DeriveManyAsync(
        string deriveId, IReadOnlyCollection<RelationScope> scopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var result = new Dictionary<string, RelationQueryResult>(StringComparer.Ordinal);
        var states = new List<BatchState>();
        foreach (var scope in scopes)
        {
            if (scope is null || string.IsNullOrWhiteSpace(scope.SourceKey)) continue;
            var key = scope.SourceKey.Trim();
            if (result.ContainsKey(key) || states.Any(s => string.Equals(s.Key, key, StringComparison.Ordinal))) continue;
            states.Add(new BatchState(key, scope));
        }

        if (states.Count == 0) return result;

        var derivation = _catalog.ResolveDerivation(deriveId);
        if (derivation is null)
        {
            foreach (var state in states) result[state.Key] = RelationQueryResults.Empty("derive-undeclared");
            return result;
        }

        foreach (var state in states) state.Type = derivation.From;

        foreach (var edgeName in derivation.Via)
        {
            var edge = _policy.FindEdge(edgeName);
            if (edge is null)
            {
                foreach (var state in states.Where(s => s.EmptyReason is null))
                    state.EmptyReason = "edge-undeclared:" + edgeName;
                break;
            }

            var probes = new List<ResourceIdentifier>();
            foreach (var state in states.Where(s => s.EmptyReason is null))
            {
                foreach (var key in state.Level)
                    probes.Add(RelationEndpoints.Create(state.Type, key, LocationFor(state.Type, state.Scope)));
            }

            var adjacency = await ReadManyAsync(probes, ct).ConfigureAwait(false);

            foreach (var state in states.Where(s => s.EmptyReason is null))
            {
                var next = new List<string>();
                foreach (var key in state.Level)
                {
                    var id = RelationEndpoints.Create(state.Type, key, LocationFor(state.Type, state.Scope));
                    if (!adjacency.TryGetValue(RelationEndpoints.KeyOf(id), out var references)) continue;

                    foreach (var reference in references)
                    {
                        if (reference.IsIncoming) continue;
                        if (!string.Equals(reference.RefType, edgeName, StringComparison.Ordinal)) continue;
                        var targetId = reference.Target.Id ?? string.Empty;
                        if (targetId.Length > 0) next.Add(targetId);
                    }
                }

                state.Level = next.Distinct(StringComparer.Ordinal).ToList();
                state.Type = string.IsNullOrWhiteSpace(edge.Target) ? state.Type : edge.Target;

                if (state.Level.Count == 0)
                    state.EmptyReason = derivation.Empty.Count > 0 ? derivation.Empty[0] : "no-edge";
            }

            if (states.All(s => s.EmptyReason is not null)) break;
        }

        foreach (var state in states)
        {
            if (state.EmptyReason is not null)
            {
                result[state.Key] = RelationQueryResults.Empty(state.EmptyReason);
                continue;
            }

            var keys = derivation.Normalize switch
            {
                "node-key" => state.Level.Select(NormalizeNodeKey).Distinct(StringComparer.Ordinal).ToList(),
                _ => state.Level,
            };

            if (string.Equals(derivation.Order, "key-ordinal", StringComparison.Ordinal))
                keys = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            result[state.Key] = keys.Count == 0
                ? RelationQueryResults.Empty(derivation.Empty.Count > 0 ? derivation.Empty[0] : "no-member")
                : new RelationQueryResult(keys, Array.Empty<string>());
        }

        return result;
    }

    /// <summary>一次批量邻接读取（有批量能力 ⇒ 恰一次；否则逐点读，键 = 端点键）。</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceReference>>> ReadManyAsync(
        IReadOnlyCollection<ResourceIdentifier> ids, CancellationToken ct)
    {
        var fallback = new Dictionary<string, IReadOnlyList<ResourceReference>>(StringComparer.Ordinal);
        if (ids.Count == 0) return fallback;

        if (_reader is IReferenceBatchReader batch)
            return await batch.GetReferencesManyAsync(ids, includeIncoming: false, ct).ConfigureAwait(false);

        foreach (var id in ids)
        {
            var key = RelationEndpoints.KeyOf(id);
            if (fallback.ContainsKey(key)) continue;
            fallback[key] = await _reader.GetReferencesAsync(id, includeIncoming: false, ct).ConfigureAwait(false);
        }

        return fallback;
    }

    private sealed class BatchState
    {
        public BatchState(string key, RelationScope scope)
        {
            Key = key;
            Scope = scope;
            Level = new List<string> { key };
        }

        public string Key { get; }

        public RelationScope Scope { get; }

        public string Type { get; set; } = string.Empty;

        public List<string> Level { get; set; }

        public string? EmptyReason { get; set; }
    }
}