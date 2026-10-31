using DataCenter.Protocol.Dataflow;
using DataCenter.Protocol.Generation;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 图查询面（D6：六问）—— 全部只读；影响面与重算计划同源（委托 <see cref="IRecomputePlanner"/>）。
/// 纪律：遇环返回 Skipped 不抛；预算到界显式 IsTruncated；无记录 ⇒ Undetermined（≠ 不存在）。
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §6.1。
/// </summary>
public sealed class GraphQuery : IGraphQuery
{
    private readonly object _gate = new();
    private readonly Dictionary<int, GraphSchemaSpec> _snapshots = new();
    private readonly IGraphSchemaRegistry _schema;
    private readonly IGraphStore _store;
    private readonly IProvenanceStore _provenance;
    private readonly IRecomputePlanner _planner;

    public GraphQuery(
        IGraphSchemaRegistry schema,
        IGraphStore store,
        IProvenanceStore provenance,
        IRecomputePlanner planner,
        IStateGeneration? generation = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));

        if (generation is not null)
            _snapshots[generation.Generation] = schema.Schema;
    }

    /// <summary>登记某代模式快照（跨代 diff 的唯一数据来源；未登记代际 ⇒ 显式不可比）。</summary>
    public void RegisterSnapshot(int generation, GraphSchemaSpec schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (_gate) _snapshots[generation] = schema;
    }

    public SubgraphResult Subgraph(string? levelId = null, int maxDepth = 0)
    {
        var depthBudget = maxDepth > 0 ? maxDepth : _schema.Schema.MaxDepth;
        var nodes = _schema.Schema.Products
            .Where(p => levelId is null || string.Equals(p.LevelId, levelId, StringComparison.Ordinal))
            .OrderBy(p => p.ProductId, StringComparer.Ordinal)
            .ToArray();

        var limit = _schema.Schema.MaxNodes;
        var truncated = nodes.Length > limit || depthBudget < 1;
        var selected = nodes.Take(limit).Select(p => p.ProductId).ToArray();
        var selectedSet = new HashSet<string>(selected, StringComparer.Ordinal);

        var edges = DataEdges()
            .Where(e => selectedSet.Contains(e.FromProductId) && selectedSet.Contains(e.ToProductId))
            .ToArray();

        var status = truncated ? GraphQueryStatus.Truncated : GraphQueryStatus.Ok;
        return new SubgraphResult(selected, edges, truncated, status, truncated ? "limit-truncated" : null);
    }

    public HierarchyWalkResult Ancestors(string productId) => Walk(productId, ancestors: true);

    public HierarchyWalkResult Descendants(string productId) => Walk(productId, ancestors: false);

    public ImpactResult Impact(string productIdOrFieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productIdOrFieldPath);

        var input = productIdOrFieldPath.Trim();
        var plan = _schema.TryGetProduct(input, out _)
            ? _planner.PlanFromProduct(input)
            : _planner.PlanFromField(input);

        return new ImpactResult(plan.Steps.Select(s => s.ProductId).ToArray(), plan.IsTruncated, plan.Status, plan.Reason);
    }

    public FieldLineageResult FieldLineage(string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        var target = fieldPath.Trim();
        var links = new List<FieldLineageLink>();

        foreach (var edge in DataEdges())
        {
            foreach (var from in edge.FromFields)
            {
                foreach (var to in edge.ToFields)
                {
                    if (!Matches(from, target) && !Matches(to, target))
                        continue;
                    links.Add(new FieldLineageLink($"{edge.FromProductId}.{from}", $"{edge.ToProductId}.{to}", edge.EdgeId, edge.Assertion));
                }
            }
        }

        if (links.Count == 0)
            return new FieldLineageResult([], GraphQueryStatus.Undetermined, $"field-lineage-undetermined:{target}");

        return new FieldLineageResult(
            links.OrderBy(l => l.EdgeId, StringComparer.Ordinal).ThenBy(l => l.FromFieldPath, StringComparer.Ordinal).ToArray(),
            GraphQueryStatus.Ok);
    }

    public GraphDiffResult Diff(int fromGeneration, int toGeneration)
    {
        GraphSchemaSpec? from, to;
        lock (_gate)
        {
            _snapshots.TryGetValue(fromGeneration, out from);
            _snapshots.TryGetValue(toGeneration, out to);
        }

        if (from is null || to is null)
        {
            return new GraphDiffResult(fromGeneration, toGeneration, [], [], [],
                GraphQueryStatus.Undetermined, "generation-snapshot-unavailable");
        }

        var fromProducts = from.Products.ToDictionary(p => p.ProductId, p => p, StringComparer.Ordinal);
        var toProducts = to.Products.ToDictionary(p => p.ProductId, p => p, StringComparer.Ordinal);

        var added = toProducts.Keys.Where(k => !fromProducts.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var removed = fromProducts.Keys.Where(k => !toProducts.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var changed = toProducts.Keys
            .Where(k => fromProducts.TryGetValue(k, out var previous) && previous.Version != toProducts[k].Version)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return new GraphDiffResult(fromGeneration, toGeneration, added, removed, changed, GraphQueryStatus.Ok);
    }

    public ProvenanceResult Provenance(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        var records = _provenance.Get(productId.Trim());
        return records.Count == 0
            ? new ProvenanceResult(productId.Trim(), null, GraphQueryStatus.Undetermined, "provenance-not-recorded")
            : new ProvenanceResult(productId.Trim(), records[^1], GraphQueryStatus.Ok);
    }

    /// <summary>归属遍历（structure.owns：From = 父 / 前驱，To = 子 / 后继）；遇环 ⇒ Skipped，预算到界 ⇒ Truncated。</summary>
    private HierarchyWalkResult Walk(string productId, bool ancestors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        var start = productId.Trim();
        var maxNodes = _schema.Schema.MaxNodes;
        var maxDepth = _schema.Schema.MaxDepth;

        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var declaration in _store.Declarations(DataEdgeKinds.StructureOwns))
        {
            var from = ancestors ? declaration.ToId : declaration.FromId;
            var to = ancestors ? declaration.FromId : declaration.ToId;
            if (!adjacency.TryGetValue(from, out var list))
                adjacency[from] = list = [];
            list.Add(to);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { start };
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        var truncated = false;
        string? cycleNode = null;

        void Visit(string node, int depth)
        {
            onPath.Add(node);

            if (adjacency.TryGetValue(node, out var nextLinks))
            {
                if (depth >= maxDepth)
                {
                    if (nextLinks.Any(link => !visited.Contains(link)))
                        truncated = true;
                }
                else
                {
                    foreach (var child in nextLinks.OrderBy(v => v, StringComparer.Ordinal))
                    {
                        if (onPath.Contains(child))
                        {
                            cycleNode = child;
                            break;
                        }

                        if (!visited.Add(child))
                            continue;

                        if (result.Count >= maxNodes)
                        {
                            truncated = true;
                            break;
                        }

                        result.Add(child);
                        Visit(child, depth + 1);

                        if (cycleNode is not null || truncated)
                            break;
                    }
                }
            }

            onPath.Remove(node);
        }

        Visit(start, 1);

        if (cycleNode is not null)
            return new HierarchyWalkResult(result.ToArray(), truncated, GraphQueryStatus.Skipped, "cycle-detected");

        return truncated
            ? new HierarchyWalkResult(result.ToArray(), true, GraphQueryStatus.Truncated, "limit-truncated")
            : new HierarchyWalkResult(result.ToArray(), false, GraphQueryStatus.Ok);
    }

    private IReadOnlyList<DataDependencyEdge> DataEdges()
    {
        var merged = new Dictionary<string, DataDependencyEdge>(StringComparer.Ordinal);

        foreach (var edge in _schema.Schema.DeclaredEdges)
        {
            if (IsData(edge.EdgeKind)) merged[edge.EdgeId] = edge;
        }

        foreach (var edge in _provenance.ObservedEdges())
        {
            if (IsData(edge.EdgeKind)) merged[edge.EdgeId] = edge;
        }

        return merged.Values.OrderBy(e => e.EdgeId, StringComparer.Ordinal).ToArray();
    }

    private static bool IsData(string kind)
        => string.Equals(kind, DataEdgeKinds.Derives, StringComparison.Ordinal)
           || string.Equals(kind, DataEdgeKinds.Aggregates, StringComparison.Ordinal);

    private static bool Matches(string field, string target)
        => string.Equals(field, target, StringComparison.Ordinal)
           || field.EndsWith("." + target, StringComparison.Ordinal);
}
