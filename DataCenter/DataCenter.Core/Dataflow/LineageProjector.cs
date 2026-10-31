using CSG.Unified;
using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 图投影器（D2）—— 顶点 + 边双通道：数据产品为顶点，声明边/观测边/结构声明为边。
/// 判据：边只进 Edges 集合，永不进 Nodes（DP2）；data.* 边的 toFields 必须 ⊆ 目标产品出字段（DP1/C17）。
/// 纪律：只读派生、输出无时间戳、不含域词汇。
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.3/§3.4。
/// </summary>
public sealed class LineageProjector : IGraphProjector
{
    /// <summary>数据产品图扩展域标识（与状态图 csg://runtime-data 并列）。</summary>
    public const string DataflowSchema = "csg://dataflow-graph";

    private const string ProductNodeKind = "data.product";

    private readonly IGraphSchemaRegistry _schema;
    private readonly IGraphStore _store;
    private readonly IProvenanceStore _provenance;

    public LineageProjector(IGraphSchemaRegistry schema, IGraphStore store, IProvenanceStore provenance)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public CanonicalForm Project(GraphProjectionRequest request)
    {
        var selected = SelectProducts(request);
        var productIds = new HashSet<string>(selected.Select(p => p.ProductId), StringComparer.Ordinal);

        var nodes = selected
            .OrderBy(p => p.ProductId, StringComparer.Ordinal)
            .Select(ToNode)
            .ToList();

        var edges = new List<CanonicalEdge>();
        int declared = 0, observed = 0, may = 0;

        foreach (var edge in _schema.Schema.DeclaredEdges.OrderBy(e => e.EdgeId, StringComparer.Ordinal))
        {
            if (!productIds.Contains(edge.FromProductId) || !productIds.Contains(edge.ToProductId)) continue;
            EnsureEndpointClosed(edge);
            edges.Add(ToCanonicalEdge(edge));
            Count(edge.Assertion, ref declared, ref observed, ref may);
        }

        foreach (var edge in _provenance.ObservedEdges().OrderBy(e => e.EdgeId, StringComparer.Ordinal))
        {
            if (!productIds.Contains(edge.FromProductId) || !productIds.Contains(edge.ToProductId)) continue;
            EnsureEndpointClosed(edge);
            edges.Add(ToCanonicalEdge(edge));
            Count(edge.Assertion, ref declared, ref observed, ref may);
        }

        foreach (var declaration in _store.Declarations().OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (!productIds.Contains(declaration.FromId) || !productIds.Contains(declaration.ToId)) continue;
            edges.Add(new CanonicalEdge
            {
                Id = declaration.Id,
                SourceId = declaration.FromId,
                TargetId = declaration.ToId,
                Label = declaration.Family,
                Type = EdgeTypeOf(declaration.Family),
                Properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["kind"] = declaration.Family,
                    ["assertion"] = DependencyAssertions.ToWire(declaration.Assertion),
                },
            });
            Count(declaration.Assertion, ref declared, ref observed, ref may);
        }

        var form = new CanonicalFormBuilder()
            .AddNodes(nodes)
            .AddEdges(edges)
            .WithMetadata("csgVersion", CsgProtocol.CurrentVersion.ToString())
            .WithMetadata("schema", DataflowSchema)
            .WithMetadata("kind", "data-product")
            .WithMetadata("scope", request.Scope ?? "all")
            .WithMetadata("products", nodes.Count)
            .WithMetadata("edges", edges.Count)
            .WithMetadata("declaredEdges", declared)
            .WithMetadata("observedEdges", observed)
            .WithMetadata("mayEdges", may)
            .Build();

        form.State = nodes.Count > 0 ? FormState.Populated : FormState.Empty;
        return form;
    }

    /// <summary>端点闭合判据（C17/DP1）：目标字段必须已在目标产品出字段中声明。</summary>
    public static void EnsureEndpointClosed(DataProductSpec product, DataDependencyEdge edge)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(edge);

        if (!string.Equals(edge.EdgeKind, DataEdgeKinds.Derives, StringComparison.Ordinal) &&
            !string.Equals(edge.EdgeKind, DataEdgeKinds.Aggregates, StringComparison.Ordinal))
            return;

        if (edge.ToFields.Count == 0)
            throw new InvalidOperationException($"边 {edge.EdgeId} 未声明目标字段（字段级依赖须闭合到字段）。");

        var declared = new HashSet<string>(product.OutFields.Select(f => f.Name), StringComparer.Ordinal);
        foreach (var field in edge.ToFields)
        {
            if (!declared.Contains(field))
                throw new InvalidOperationException(
                    $"越权写：边 {edge.EdgeId} 的目标字段 '{field}' 未在目标产品 '{product.ProductId}' 的出字段中声明。");
        }
    }

    private void EnsureEndpointClosed(DataDependencyEdge edge)
    {
        if (!_schema.TryGetProduct(edge.ToProductId, out var target))
            throw new InvalidOperationException(
                $"越权写：边 {edge.EdgeId} 的目标产品 '{edge.ToProductId}' 未登记（DP1：端点必为已登记产品）。");

        EnsureEndpointClosed(target, edge);
    }

    private IReadOnlyList<DataProductSpec> SelectProducts(GraphProjectionRequest request)
    {
        IEnumerable<DataProductSpec> products = _schema.Schema.Products;
        if (request.ProductIds is { Count: > 0 } requested)
        {
            var wanted = new HashSet<string>(requested, StringComparer.Ordinal);
            products = products.Where(p => wanted.Contains(p.ProductId));
        }

        return products.ToArray();
    }

    private static CanonicalNode ToNode(DataProductSpec product) => new()
    {
        Id = product.ProductId,
        Label = product.ProductId,
        Type = CanonicalNodeType.Data,
        Properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = ProductNodeKind,
            ["level"] = product.LevelId,
            ["role"] = product.Role,
            ["stage"] = product.Stage,
            ["version"] = product.Version,
            ["compatibility"] = product.Compatibility,
            ["idempotencyKey"] = product.IdempotencyKey,
            ["inFields"] = product.InFields.Select(f => f.Name).ToArray(),
            ["outFields"] = product.OutFields.Select(f => f.Name).ToArray(),
        },
    };

    private static CanonicalEdge ToCanonicalEdge(DataDependencyEdge edge)
    {
        // 键序固定 + 声明属性按 Ordinal 排序写入 ⇒ ToJson() 字节级幂等（V6）。
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = edge.EdgeKind,
            ["assertion"] = DependencyAssertions.ToWire(edge.Assertion),
            ["fromFields"] = edge.FromFields.ToArray(),
            ["toFields"] = edge.ToFields.ToArray(),
        };

        if (!string.IsNullOrWhiteSpace(edge.Label)) properties["label"] = edge.Label;

        if (edge.Properties is { Count: > 0 } declared)
        {
            foreach (var key in declared.Keys.OrderBy(k => k, StringComparer.Ordinal))
                properties[key] = declared[key];
        }

        return new CanonicalEdge
        {
            Id = edge.EdgeId,
            SourceId = edge.FromProductId,
            TargetId = edge.ToProductId,
            Label = edge.EdgeKind,
            Type = EdgeTypeOf(edge.EdgeKind),
            Properties = properties,
        };
    }

    private static CanonicalEdgeType EdgeTypeOf(string kind) => kind switch
    {
        DataEdgeKinds.StructureOwns => CanonicalEdgeType.Composition,
        DataEdgeKinds.ControlSequence => CanonicalEdgeType.ControlFlow,
        DataEdgeKinds.Derives => CanonicalEdgeType.DataFlow,
        DataEdgeKinds.Aggregates => CanonicalEdgeType.Dependency,
        _ => CanonicalEdgeType.Reference,
    };

    private static void Count(DependencyAssertion assertion, ref int declared, ref int observed, ref int may)
    {
        switch (assertion)
        {
            case DependencyAssertion.Declared:
                declared++;
                break;
            case DependencyAssertion.Observed:
                observed++;
                break;
            default:
                may++;
                break;
        }
    }
}
