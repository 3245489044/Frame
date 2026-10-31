using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 图模式注册表实现（DF7 / DP1）—— 装配期注册、运行期只读；形态同 <c>GraphRegistry</c>。
/// <para>
/// 注册即校验（fail-fast，不静默）：产品键唯一、边族断言集非空、声明边断言恒 <c>declared</c>。
/// 层 / 角色 / 边族均为开放字符串（层是数据不是类型），不在此处闭枚举。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §4.1 / §5.1。
/// </summary>
public sealed class GraphSchemaRegistry : IGraphSchemaRegistry
{
    private readonly Dictionary<string, DataProductSpec> _products;

    /// <param name="schema">初始模式声明；null = 空声明（未接入任何域）。</param>
    public GraphSchemaRegistry(GraphSchemaSpec? schema = null)
    {
        Schema = schema ?? GraphSchemaSpec.Empty;
        _products = new Dictionary<string, DataProductSpec>(StringComparer.Ordinal);

        foreach (var product in Schema.Products)
        {
            if (string.IsNullOrWhiteSpace(product.ProductId))
                throw new ArgumentException("产品声明缺 ProductId（禁无名产品）。", nameof(schema));
            if (!_products.TryAdd(product.ProductId, product))
                throw new InvalidOperationException($"产品键重复登记：{product.ProductId}（每产品恰一条声明）。");
        }
    }

    /// <summary>空注册表（未接入任何域；投影产出空图）。</summary>
    public static GraphSchemaRegistry Empty { get; } = new();

    public GraphSchemaSpec Schema { get; }

    public bool TryGetProduct(string productId, out DataProductSpec product)
    {
        product = null!;
        return !string.IsNullOrWhiteSpace(productId) && _products.TryGetValue(productId.Trim(), out product!);
    }

    /// <summary>增量复制并追加层声明（原注册表不变）。</summary>
    public GraphSchemaRegistry WithLevel(GraphLevelSpec level)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentException.ThrowIfNullOrWhiteSpace(level.LevelId);

        var levels = Schema.Levels.Where(l => !string.Equals(l.LevelId, level.LevelId, StringComparison.Ordinal))
            .Append(level)
            .OrderBy(l => l.LevelId, StringComparer.Ordinal)
            .ToArray();
        return With(Schema with { Levels = levels });
    }

    /// <summary>增量复制并追加边族声明（允许断言集须非空）。</summary>
    public GraphSchemaRegistry WithEdgeFamily(GraphEdgeFamilySpec family)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(family.Family);
        if (family.AllowedAssertions is null || family.AllowedAssertions.Count == 0)
            throw new ArgumentException($"边族 '{family.Family}' 未声明允许断言集（禁无限强度边族）。", nameof(family));

        var families = Schema.EdgeFamilies.Where(f => !string.Equals(f.Family, family.Family, StringComparison.Ordinal))
            .Append(family)
            .OrderBy(f => f.Family, StringComparer.Ordinal)
            .ToArray();
        return With(Schema with { EdgeFamilies = families });
    }

    /// <summary>增量复制并追加产品声明（键唯一）。</summary>
    public GraphSchemaRegistry WithProduct(DataProductSpec product)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(product.ProductId);
        if (_products.ContainsKey(product.ProductId))
            throw new InvalidOperationException($"产品键重复登记：{product.ProductId}（每产品恰一条声明）。");

        var products = Schema.Products.Append(product)
            .OrderBy(p => p.ProductId, StringComparer.Ordinal)
            .ToArray();
        return With(Schema with { Products = products });
    }

    /// <summary>增量复制并追加声明边（断言恒 declared：声明面不得产出 observed/may）。</summary>
    public GraphSchemaRegistry WithDeclaredEdge(DataDependencyEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentException.ThrowIfNullOrWhiteSpace(edge.EdgeId);
        if (Schema.DeclaredEdges.Any(e => string.Equals(e.EdgeId, edge.EdgeId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"声明边键重复：{edge.EdgeId}。");

        var edges = Schema.DeclaredEdges
            .Append(edge with { Assertion = DependencyAssertion.Declared })
            .OrderBy(e => e.EdgeId, StringComparer.Ordinal)
            .ToArray();
        return With(Schema with { DeclaredEdges = edges });
    }

    /// <summary>替换处理预算（MaxNodes/MaxDepth 为强制边界）。</summary>
    public GraphSchemaRegistry WithBudget(int maxNodes, int maxDepth)
    {
        if (maxNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maxNodes), maxNodes, "MaxNodes 必须为正。");
        if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "MaxDepth 必须为正。");
        return With(Schema with { MaxNodes = maxNodes, MaxDepth = maxDepth });
    }

    private GraphSchemaRegistry With(GraphSchemaSpec schema) => new(schema);
}
