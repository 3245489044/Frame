using System.Security.Cryptography;
using System.Text;
using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 受影响面重算规划器（D5）—— 纯函数：同输入同输出，可整体关闭回归「整链重跑」。
/// 闭包 = 起点 descendants（沿 data.* 边）∩ 预算（MaxNodes/MaxDepth）；序 = 拓扑序（Kahn，成环显式 Skipped）；
/// 到界 ⇒ IsTruncated 显式可见。
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.5 / DP6。
/// </summary>
public sealed class RecomputePlanner : IRecomputePlanner
{
    private readonly IGraphSchemaRegistry _schema;
    private readonly IProvenanceStore _provenance;

    public RecomputePlanner(IGraphSchemaRegistry schema, IProvenanceStore provenance)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public RecomputePlan PlanFromProduct(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        var root = productId.Trim();
        if (!_schema.TryGetProduct(root, out _))
            return new RecomputePlan([], false, GraphQueryStatus.Undetermined, $"product-not-registered:{root}");

        return BuildPlan([root], AllEdges());
    }

    public RecomputePlan PlanFromField(string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        var target = fieldPath.Trim();
        var edges = AllEdges();
        var roots = edges
            .Where(e => MatchesField(e.FromFields, target) || MatchesField(e.ToFields, target))
            .Select(e => e.FromProductId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (roots.Length == 0)
            return new RecomputePlan([], false, GraphQueryStatus.Undetermined, $"field-not-registered:{target}");

        return BuildPlan(roots, edges);
    }

    public string ComputeIdempotencyKey(DataProductSpec product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var inFields = string.Join(";", product.InFields
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => $"{f.Name}:{f.Type}:{(f.Required ? "1" : "0")}"));
        var payload = $"{product.ProductId}|{product.LevelId}|{inFields}";

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private RecomputePlan BuildPlan(IReadOnlyList<string> roots, IReadOnlyList<DataDependencyEdge> edges)
    {
        var adjacency = edges
            .GroupBy(e => e.FromProductId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ToProductId).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var maxNodes = _schema.Schema.MaxNodes;
        var maxDepth = _schema.Schema.MaxDepth;
        var collected = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        var frontier = new List<(string Id, int Depth)>();
        foreach (var root in roots)
        {
            if (collected.Add(root))
                frontier.Add((root, 1));
        }

        while (frontier.Count > 0)
        {
            var next = new List<(string Id, int Depth)>();
            foreach (var (id, depth) in frontier.OrderBy(f => f.Id, StringComparer.Ordinal))
            {
                if (!adjacency.TryGetValue(id, out var downstream))
                    continue;

                if (depth >= maxDepth)
                {
                    if (downstream.Any(child => !collected.Contains(child)))
                        truncated = true;
                    continue;
                }

                foreach (var child in downstream)
                {
                    if (collected.Count >= maxNodes)
                    {
                        truncated = true;
                        break;
                    }

                    if (collected.Add(child))
                        next.Add((child, depth + 1));
                }
            }

            frontier = next;
        }

        var nodes = collected.OrderBy(v => v, StringComparer.Ordinal).ToArray();
        var (ordered, cyclic) = Topological(nodes, edges);

        var steps = ordered.Select((id, index) => new RecomputeStep(id, index)).ToArray();
        var status = cyclic
            ? GraphQueryStatus.Skipped
            : truncated ? GraphQueryStatus.Truncated : GraphQueryStatus.Ok;
        var reason = cyclic ? "cycle-detected" : truncated ? "limit-truncated" : null;

        return new RecomputePlan(steps, truncated, status, reason);
    }

    /// <summary>Kahn 拓扑序（同层按 Ordinal 升序 ⇒ 输出确定；剩余节点非空 ⇒ 成环）。</summary>
    private static (IReadOnlyList<string> Ordered, bool Cyclic) Topological(
        IReadOnlyList<string> nodes,
        IReadOnlyList<DataDependencyEdge> edges)
    {
        var inSet = new HashSet<string>(nodes, StringComparer.Ordinal);
        var indegree = nodes.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.ToDictionary(n => n, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            if (!inSet.Contains(edge.FromProductId) || !inSet.Contains(edge.ToProductId))
                continue;
            if (string.Equals(edge.FromProductId, edge.ToProductId, StringComparison.Ordinal))
                continue;

            outgoing[edge.FromProductId].Add(edge.ToProductId);
            indegree[edge.ToProductId]++;
        }

        var ready = new SortedSet<string>(nodes.Where(n => indegree[n] == 0), StringComparer.Ordinal);
        var ordered = new List<string>(nodes.Count);

        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            ordered.Add(current);

            foreach (var next in outgoing[current])
            {
                if (--indegree[next] == 0)
                    ready.Add(next);
            }
        }

        return (ordered, ordered.Count < nodes.Count);
    }

    private IReadOnlyList<DataDependencyEdge> AllEdges()
    {
        var merged = new Dictionary<string, DataDependencyEdge>(StringComparer.Ordinal);

        foreach (var edge in _schema.Schema.DeclaredEdges)
        {
            if (IsDataEdge(edge.EdgeKind))
                merged[edge.EdgeId] = edge;
        }

        foreach (var edge in _provenance.ObservedEdges())
        {
            if (IsDataEdge(edge.EdgeKind))
                merged[edge.EdgeId] = edge;
        }

        return merged.Values.OrderBy(e => e.EdgeId, StringComparer.Ordinal).ToArray();
    }

    private static bool IsDataEdge(string kind)
        => string.Equals(kind, DataEdgeKinds.Derives, StringComparison.Ordinal)
           || string.Equals(kind, DataEdgeKinds.Aggregates, StringComparison.Ordinal);

    private static bool MatchesField(IReadOnlyList<string> fields, string target)
        => fields.Any(f => string.Equals(f, target, StringComparison.Ordinal)
                           || f.EndsWith("." + target, StringComparison.Ordinal));
}
