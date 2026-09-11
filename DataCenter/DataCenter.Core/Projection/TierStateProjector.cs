using CSG.Unified;
using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Projection;
using DataCenter.Protocol.Registry;

namespace Operation.StateBlock.Projection;

/// <summary>
/// 通用结构投影器（D1 投影基座）—— 把分层状态（tier × scope × 键）派生为语言无关的 <see cref="CanonicalForm"/>：
/// <list type="bullet">
/// <item>节点：每个状态条目 = 一个 <c>state.value</c> 节点（属性含 value/tier/scope/key/namespace/domain）</item>
/// <item>分区锚点：每个 (domain, tier, scope) = 一个 <c>state.scope</c> 节点（承载归属结构边）</item>
/// <item>边：<c>structure.owns</c>（锚点 → 值节点），DF5 断言成立（两端同源派生，不存在悬空）</item>
/// </list>
/// 纪律：只读派生（绝不回写状态）；输出<b>确定性</b>（节点/边按 tier→scope→key 全序排序，无时间戳），
/// 故 <c>Project</c> 幂等可断言（观测时间戳属使用层 dump 时附加，不进投影产物）。
/// 权威: 设计文档 §7 D1/§4.4（seqRange/writers 随 D2 追踪字段落地）。
/// </summary>
public sealed class TierStateProjector : IStateProjector
{
    /// <summary>CSG 运行时数据扩展域标识（Profile 常量；对齐 schemas/csg-runtime-data.schema.json）。</summary>
    public const string RuntimeDataSchema = "csg://runtime-data";

    private const string ScopeNodeKind = "state.scope";
    private const string ValueNodeKind = "state.value";
    private const string OwnsEdgeKind = "structure.owns";

    private readonly IStateEntrySource _source;
    private readonly IStateRegistry _registry;
    private readonly IStateValueCodec _codec;
    private readonly IStateGeneration _generation;

    public TierStateProjector(
        IStateEntrySource source,
        IStateRegistry registry,
        IStateValueCodec codec,
        IStateGeneration generation)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
    }

    public CanonicalForm Project(ProjectionRequest request)
    {
        var scan = _source.Enumerate(request.Scope);
        var requestedTiers = request.Tiers is { Count: > 0 }
            ? new HashSet<string>(request.Tiers, StringComparer.Ordinal)
            : null;

        var entries = new List<(StateEntry Entry, StateUriRef Ref, string TierId)>();
        var filtered = 0;
        foreach (var entry in scan.Entries)
        {
            var tierId = StateUriMapper.TierId(entry.Tier);
            if (requestedTiers is not null && !requestedTiers.Contains(tierId))
            {
                filtered++;
                continue;
            }

            entries.Add((entry, StateUriMapper.Resolve(entry), tierId));
        }

        // 确定性排序（幂等判据）：tier → scope → key → domain
        entries.Sort(static (a, b) =>
        {
            var cmp = string.CompareOrdinal(a.TierId, b.TierId);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Ref.Scope, b.Ref.Scope);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Ref.Key, b.Ref.Key);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Ref.Domain, b.Ref.Domain);
        });

        var nodes = new List<CanonicalNode>(entries.Count + 1);
        var edges = new List<CanonicalEdge>(entries.Count);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var domains = new HashSet<string>(StringComparer.Ordinal);
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        var unregisteredTiers = 0;

        foreach (var (entry, reference, tierId) in entries)
        {
            domains.Add(reference.Domain);
            scopes.Add(reference.Scope);
            if (!_registry.IsTierRegistered(tierId))
                unregisteredTiers++;

            EmitScopeAnchor(nodes, emitted, reference);
            EmitValueNode(nodes, edges, emitted, entry, reference);
        }

        return BuildForm(nodes, edges, request, scan.UnparsedKeys, unregisteredTiers, filtered, domains, scopes);
    }

    private static void EmitScopeAnchor(List<CanonicalNode> nodes, HashSet<string> emitted, StateUriRef reference)
    {
        var scopeNodeId = StateUriMapper.ScopeNodeUri(reference);
        if (!emitted.Add(scopeNodeId))
            return;

        nodes.Add(new CanonicalNode
        {
            Id = scopeNodeId,
            Label = $"{reference.Domain}/{reference.TierId}/{reference.Scope}",
            Type = CanonicalNodeType.State,
            Properties = new Dictionary<string, object?>
            {
                ["kind"] = ScopeNodeKind,
                ["domain"] = reference.Domain,
                ["tier"] = reference.TierId,
                ["scope"] = reference.Scope,
                ["key"] = StateUriMapper.ScopeNodeKey,
            },
        });
    }

    private void EmitValueNode(
        List<CanonicalNode> nodes,
        List<CanonicalEdge> edges,
        HashSet<string> emitted,
        StateEntry entry,
        StateUriRef reference)
    {
        var scopeNodeId = StateUriMapper.ScopeNodeUri(reference);
        if (!emitted.Add(reference.Uri))
            return;

        nodes.Add(new CanonicalNode
        {
            Id = reference.Uri,
            Label = reference.Key,
            Type = CanonicalNodeType.State,
            Properties = new Dictionary<string, object?>
            {
                ["kind"] = ValueNodeKind,
                ["value"] = _codec.Encode(entry.Value),
                ["tier"] = reference.TierId,
                ["scope"] = reference.Scope,
                ["key"] = reference.Key,
                ["namespace"] = reference.Namespace,
                ["domain"] = reference.Domain,
            },
        });

        edges.Add(new CanonicalEdge
        {
            Id = $"{scopeNodeId}|{OwnsEdgeKind}|{reference.Uri}",
            SourceId = scopeNodeId,
            TargetId = reference.Uri,
            Label = OwnsEdgeKind,
            Type = CanonicalEdgeType.Composition,
            Properties = new Dictionary<string, object?>
            {
                ["kind"] = OwnsEdgeKind,
                ["assertion"] = "Asserted",
            },
        });
    }

    private CanonicalForm BuildForm(
        List<CanonicalNode> nodes,
        List<CanonicalEdge> edges,
        ProjectionRequest request,
        int unparsedKeys,
        int unregisteredTierEntries,
        int filteredByTier,
        HashSet<string> domains,
        HashSet<string> scopes)
    {
        var builder = new CanonicalFormBuilder()
            .AddNodes(nodes)
            .AddEdges(edges)
            .WithMetadata("csgVersion", CsgProtocol.CurrentVersion.ToString())
            .WithMetadata("schema", RuntimeDataSchema)
            .WithMetadata("kind", "state-value")
            .WithMetadata("domain", Single(domains, "domain"))
            .WithMetadata("tiers", _registry.Tiers.OrderBy(v => v, StringComparer.Ordinal).ToArray())
            .WithMetadata("scope", request.Scope ?? Single(scopes, "scope"))
            .WithMetadata("generation", _generation.Generation)
            .WithMetadata("schemaVersion", _generation.SchemaVersion)
            .WithMetadata("unparsedKeys", unparsedKeys)
            .WithMetadata("unregisteredTierEntries", unregisteredTierEntries)
            .WithMetadata("filteredByTier", filteredByTier);

        if (_generation.Fingerprint is { Length: > 0 } fingerprint)
            builder.WithMetadata("fingerprint", fingerprint);

        var form = builder.Build();
        form.State = nodes.Count > 0 ? FormState.Populated : FormState.Empty;
        return form;
    }

    private static string Single(HashSet<string> values, string label) => values.Count switch
    {
        0 => $"empty-{label}",
        1 => values.First(),
        _ => "mixed",
    };
}
