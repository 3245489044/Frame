using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 查询面**通用追踪装饰器**（读即追踪）—— 把任意 <see cref="IGraphQuery"/> 的每次读动作记为一条
/// provenance（四要素：writer / at / seq / causedBy），observed 边只取结果中的 <see cref="DataEdgeKinds.Derives"/>
/// / <see cref="DataEdgeKinds.Aggregates"/> 边（归属/顺序边不得伪装成观测，DP5）。
/// <para>
/// 通用性：本类不认识任何域、任何查询实现（装饰即可用）；领域侧零打点代码 —— 打点位置 = 1（组合根装饰一次）。
/// 纪律：不改写结果（只读透传）；盖章失败即上抛（禁吞）。
/// </para>
/// </summary>
public sealed class ProvenanceTappedQuery : IGraphQuery
{
    /// <summary>无产品键参数时的占位产品键（Q1/Q5 等按层或代际查询；不伪造业务键）。</summary>
    public const string AnonymousProductId = "graph";

    private readonly IGraphQuery _inner;
    private readonly IProvenanceStore _provenance;
    private readonly string _writerPrefix;

    /// <param name="inner">被装饰的查询面。</param>
    /// <param name="provenance">盖章端口（唯一写入口 <see cref="IProvenanceStore.Record"/>）。</param>
    /// <param name="writerPrefix">写入者前缀（缺省 <c>graph-query</c>；实际 writer = 前缀 + 问题号）。</param>
    public ProvenanceTappedQuery(IGraphQuery inner, IProvenanceStore provenance, string writerPrefix = "graph-query")
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        _writerPrefix = string.IsNullOrWhiteSpace(writerPrefix) ? "graph-query" : writerPrefix.Trim();
    }

    /// <inheritdoc />
    public SubgraphResult Subgraph(string? levelId = null, int maxDepth = 0)
    {
        var result = _inner.Subgraph(levelId, maxDepth);
        Record("Q1", levelId, maxDepth, result.Edges);
        return result;
    }

    /// <inheritdoc />
    public HierarchyWalkResult Ancestors(string productId)
    {
        var result = _inner.Ancestors(productId);
        Record("Q2", productId, result.ProductIds.Count, null);
        return result;
    }

    /// <inheritdoc />
    public HierarchyWalkResult Descendants(string productId)
    {
        var result = _inner.Descendants(productId);
        Record("Q2", productId, result.ProductIds.Count, null);
        return result;
    }

    /// <inheritdoc />
    public ImpactResult Impact(string productIdOrFieldPath)
    {
        var result = _inner.Impact(productIdOrFieldPath);
        Record("Q4", productIdOrFieldPath, result.ProductIds.Count, null);
        return result;
    }

    /// <inheritdoc />
    public FieldLineageResult FieldLineage(string fieldPath)
    {
        var result = _inner.FieldLineage(fieldPath);
        Record("Q3", fieldPath, result.Links.Count, null);
        return result;
    }

    /// <inheritdoc />
    public GraphDiffResult Diff(int fromGeneration, int toGeneration)
    {
        var result = _inner.Diff(fromGeneration, toGeneration);
        Record("Q5", $"{fromGeneration}..{toGeneration}", 0, null);
        return result;
    }

    /// <inheritdoc />
    public ProvenanceResult Provenance(string productId)
    {
        var result = _inner.Provenance(productId);
        Record("Q6", productId, 0, null);
        return result;
    }

    /// <summary>盖章（一次读 = 一条记录；observed 边仅取数据依赖族）。</summary>
    private void Record(string question, string? subject, int depth, IReadOnlyList<DataDependencyEdge>? edges)
    {
        var productId = string.IsNullOrWhiteSpace(subject) ? AnonymousProductId : subject!.Trim();
        var observed = edges is { Count: > 0 }
            ? edges.Where(e => IsObservableDataEdge(e.EdgeKind)).ToArray()
            : [];

        _provenance.Record(productId, $"{_writerPrefix}:{question}", subject, Math.Max(depth, 0), observed);
    }

    private static bool IsObservableDataEdge(string kind)
        => string.Equals(kind, DataEdgeKinds.Derives, StringComparison.Ordinal)
           || string.Equals(kind, DataEdgeKinds.Aggregates, StringComparison.Ordinal);
}
