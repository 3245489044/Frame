using System.Text.RegularExpressions;
using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>声明合成诊断（结构事实：码 + 归因源 + 文本；禁静默丢弃）。</summary>
/// <param name="Code">诊断码（D01…；门禁/单测按码断言）。</param>
/// <param name="Message">诊断文本。</param>
/// <param name="SourceId">归因声明源（null = 合成器级）。</param>
public sealed record DeclarativeGraphDiagnostic(string Code, string Message, string? SourceId = null);

/// <summary>声明合成结果（**总是**交回可检视的 schema + 诊断；错误经 <see cref="HasErrors"/> 显式）。</summary>
/// <param name="Schema">合成出的图模式声明（含违规项时仍可检视，但不得作为放行依据）。</param>
/// <param name="Diagnostics">诊断集（空 = 全绿）。</param>
public sealed record DeclarativeGraphComposition(
    GraphSchemaSpec Schema,
    IReadOnlyList<DeclarativeGraphDiagnostic> Diagnostics)
{
    /// <summary>是否存在诊断（error 级 ⇒ 不得接入 Boot）。</summary>
    public bool HasErrors => Diagnostics.Count > 0;
}

/// <summary>声明 ↔ 图模式差异（`GetDiff=0` 的判据载体；门禁与单测共用同一实现）。</summary>
/// <param name="Added">声明有、模式无的键（产品/边）。</param>
/// <param name="Removed">模式有、声明无的键。</param>
/// <param name="Changed">两端都有但形状不一致的键。</param>
public sealed record DeclarativeGraphDiff(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed)
{
    /// <summary>无差异（可机械复算的放行判据）。</summary>
    public bool IsZero => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

/// <summary>声明合成违规（fail-fast 载体；携带全部诊断，禁只报第一条）。</summary>
public sealed class DeclarativeGraphException : InvalidOperationException
{
    public DeclarativeGraphException(IReadOnlyList<DeclarativeGraphDiagnostic> diagnostics)
        : base("声明合成失败（" + diagnostics.Count + " 条）：" + Environment.NewLine + "  "
               + string.Join(Environment.NewLine + "  ",
                   diagnostics.Select(d => $"{d.Code} [{d.SourceId ?? "composer"}] {d.Message}")))
        => Diagnostics = diagnostics;

    /// <summary>全部诊断（不改写、不截断）。</summary>
    public IReadOnlyList<DeclarativeGraphDiagnostic> Diagnostics { get; }
}
/// <summary>
/// 声明式产品图合成器（**通用**：不认识任何具体声明面 / 域词汇 / 呈现词）。
/// <para>
/// 职责：<see cref="IDeclarationSource"/> 集 → <see cref="GraphSchemaSpec"/>，并在合成期执行可机械复算的判据：
/// D01 产品键空/形态非法、D02 产品键重复、D03 层 Id 重复、D04 边端点未登记、D05 data.* 边目标字段越权/缺失、
/// D06 幂等键缺省或含易变分量、D07 声明面出现非 declared 断言、D08 字段集非法、D09 边键重复、D10 空源项、
/// D11 边属性非法（键形态/重复/空值）。
/// </para>
/// <para>
/// 纪律：① 输出确定性（产品/层/边按 Ordinal 升序）；② 空源 ⇒ <see cref="GraphSchemaSpec.Empty"/>（不伪造）；
/// ③ 违规只报不改（静默修补等于放行）。
/// </para>
/// </summary>
public static class DeclarativeGraphComposer
{
    private static readonly Regex ProductKeyPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);

    private static readonly Regex FieldNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    private static readonly Regex PropertyKeyPattern = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    /// <summary>框架保留属性键（投影层固定键；声明面同名 ⇒ D11，禁静默覆盖）。</summary>
    private static readonly HashSet<string> ReservedPropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "assertion", "label", "fromFields", "toFields",
    };

    /// <summary>合成声明集（错误经诊断返回，不抛；需要 fail-fast 用 <see cref="ComposeOrThrow"/>）。</summary>
    /// <param name="sources">声明源集（null 项跳过并记 D10）。</param>
    /// <param name="maxNodes">影响面 / 子图最大节点数（强制边界）。</param>
    /// <param name="maxDepth">最大遍历深度（根 = 1）。</param>
    public static DeclarativeGraphComposition Compose(
        IEnumerable<IDeclarationSource> sources, int maxNodes = 10_000, int maxDepth = 64)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var diagnostics = new List<DeclarativeGraphDiagnostic>();
        var products = new List<DataProductSpec>();
        var levels = new List<GraphLevelSpec>();
        var edges = new List<DataDependencyEdge>();

        var productByKey = new Dictionary<string, DataProductSpec>(StringComparer.Ordinal);
        var levelIds = new HashSet<string>(StringComparer.Ordinal);
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            if (source is null)
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic("D10", "声明源集含 null 项（已跳过）。"));
                continue;
            }

            var declared = source.Declare() ?? DeclaredGraph.Empty;
            var sourceId = source.SourceId;

            foreach (var level in declared.Levels)
            {
                if (level is null || string.IsNullOrWhiteSpace(level.LevelId))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic("D03", "层声明缺 LevelId。", sourceId));
                    continue;
                }

                if (!levelIds.Add(level.LevelId))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic("D03", $"层 Id 重复：{level.LevelId}。", sourceId));
                    continue;
                }

                levels.Add(new GraphLevelSpec(
                    level.LevelId,
                    string.IsNullOrWhiteSpace(level.EntityType) ? level.LevelId : level.EntityType,
                    level.ContainsEdgeFamily,
                    level.ProcessId));
            }

            foreach (var product in declared.Products)
            {
                if (product is null || string.IsNullOrWhiteSpace(product.ProductId))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic("D01", "产品声明缺 ProductId。", sourceId));
                    continue;
                }

                if (!ProductKeyPattern.IsMatch(product.ProductId))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic(
                        "D01", $"产品键形态非法：{product.ProductId}（须以字母/下划线起，点分段）。", sourceId));
                    continue;
                }

                if (productByKey.ContainsKey(product.ProductId))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic("D02", $"产品键重复：{product.ProductId}。", sourceId));
                    continue;
                }

                var outFields = NormalizeFields(product.OutFields, sourceId, product.ProductId, "outFields", diagnostics);
                var inFields = NormalizeFields(product.InFields, sourceId, product.ProductId, "inFields", diagnostics);

                if (outFields.Count == 0)
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic(
                        "D08", $"出字段为空：{product.ProductId}（对外承诺必非空）。", sourceId));
                }

                if (string.IsNullOrWhiteSpace(product.IdempotencyKey))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic("D06", $"缺幂等键：{product.ProductId}。", sourceId));
                }

                var spec = new DataProductSpec(
                    product.ProductId,
                    string.IsNullOrWhiteSpace(product.LevelId) ? "unknown" : product.LevelId,
                    string.IsNullOrWhiteSpace(product.Role) ? ProductRoles.Source : product.Role,
                    inFields,
                    outFields,
                    product.IdempotencyKey ?? string.Empty,
                    product.Version <= 0 ? 1 : product.Version,
                    product.Compatibility,
                    product.Stage,
                    product.DependsOn,
                    product.Migration);

                productByKey[product.ProductId] = spec;
                products.Add(spec);
            }




            foreach (var edge in declared.Edges)
            {
                if (edge is null) continue;

                if (edge.Assertion != DependencyAssertion.Declared)
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic(
                        "D07", "声明面只允许 declared 断言（observed/may 不得由声明产生）。", sourceId));
                    continue;
                }

                var edgeId = EdgeIdOf(edge);
                if (!edgeIds.Add(edgeId))
                {
                    diagnostics.Add(new DeclarativeGraphDiagnostic("D09", $"边键重复：{edgeId}。", sourceId));
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(edge.Label) ? null : edge.Label.Trim();
                edges.Add(new DataDependencyEdge(
                    edgeId,
                    edge.FromProductId,
                    edge.FromFields ?? [],
                    edge.ToProductId,
                    edge.ToFields ?? [],
                    edge.EdgeKind,
                    edge.Assertion,
                    label,
                    NormalizeProperties(edge.Properties, edgeId, sourceId, diagnostics)));
            }
        }

        ValidateClosure(edges, productByKey, diagnostics);

        var schema = new GraphSchemaSpec(
            levels.OrderBy(l => l.LevelId, StringComparer.Ordinal).ToArray(),
            BuildEdgeFamilies(),
            products.OrderBy(p => p.ProductId, StringComparer.Ordinal).ToArray(),
            edges.OrderBy(e => e.EdgeId, StringComparer.Ordinal).ToArray(),
            maxNodes,
            maxDepth);

        return new DeclarativeGraphComposition(schema, diagnostics);
    }

    /// <summary>合成并在有错误时 fail-fast（装配期唯一放行口径）。</summary>
    /// <param name="sources">声明源集。</param>
    /// <param name="maxNodes">最大节点数。</param>
    /// <param name="maxDepth">最大深度。</param>
    public static GraphSchemaSpec ComposeOrThrow(
        IEnumerable<IDeclarationSource> sources, int maxNodes = 10_000, int maxDepth = 64)
    {
        var result = Compose(sources, maxNodes, maxDepth);
        EnsureValid(result);
        return result.Schema;
    }

    /// <summary>校验合成结果（有错误即抛 <see cref="DeclarativeGraphException"/>，携带全部诊断）。</summary>
    /// <param name="result">合成结果。</param>
    public static void EnsureValid(DeclarativeGraphComposition result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.HasErrors) throw new DeclarativeGraphException(result.Diagnostics);
    }

    /// <summary>
    /// 边规范键（层内唯一；确定性形态 <c>{from}|{kind}|{to}</c>，带标签时追加 <c>|{label}</c>）。
    /// <para>无标签边保持旧形态（既有声明/代际快照零漂移）；标签用于同一 (端点, 边族) 上的多重关系并存。</para>
    /// </summary>
    /// <param name="edge">边声明。</param>
    public static string EdgeIdOf(EdgeDeclaration edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        var label = string.IsNullOrWhiteSpace(edge.Label) ? null : edge.Label.Trim();
        return label is null
            ? $"{edge.FromProductId}|{edge.EdgeKind}|{edge.ToProductId}"
            : $"{edge.FromProductId}|{edge.EdgeKind}|{edge.ToProductId}|{label}";
    }

    /// <summary>
    /// 边属性归一（键 Ordinal 排序 ⇒ 输出确定；空属性 ⇒ null）。
    /// <para>违规只报不改：键形态非法/忽略大小写重复/取值为空 ⇒ D11（静默修补等于放行）。</para>
    /// </summary>
    private static IReadOnlyDictionary<string, string?>? NormalizeProperties(
        IReadOnlyDictionary<string, string?>? properties, string edgeId, string? sourceId,
        List<DeclarativeGraphDiagnostic> diagnostics)
    {
        if (properties is null || properties.Count == 0) return null;

        var sorted = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in properties)
        {
            if (string.IsNullOrWhiteSpace(key) || !PropertyKeyPattern.IsMatch(key))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D11", $"边 {edgeId} 的属性键形态非法：'{key}'。", sourceId));
                continue;
            }

            if (!seen.Add(key))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D11", $"边 {edgeId} 的属性键重复：{key}。", sourceId));
                continue;
            }

            if (ReservedPropertyKeys.Contains(key))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D11", $"边 {edgeId} 的属性键与框架保留键冲突：{key}。", sourceId));
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D11", $"边 {edgeId} 的属性 '{key}' 取值为空（无值即不声明）。", sourceId));
                continue;
            }

            sorted[key] = value;
        }

        return sorted.Count == 0 ? null : sorted;
    }

    /// <summary>
    /// 声明 ↔ 模式差异（DP3 可机械重算）：产品比对形状（层/角色/阶段/兼容性/幂等键/版本/字段名序），
    /// 边比对种类、端点、标签、字段序、断言与属性集；差异键 = 产品键与边 Id。
    /// </summary>
    /// <param name="sources">声明源集。</param>
    /// <param name="schema">目标模式（Boot 实际生效的那份）。</param>
    public static DeclarativeGraphDiff Diff(IEnumerable<IDeclarationSource> sources, GraphSchemaSpec schema)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(schema);

        var composed = Compose(sources).Schema;
        var declaredProducts = composed.Products.ToDictionary(p => p.ProductId, StringComparer.Ordinal);
        var actualProducts = schema.Products.ToDictionary(p => p.ProductId, StringComparer.Ordinal);

        var added = declaredProducts.Keys.Where(k => !actualProducts.ContainsKey(k)).ToList();
        var removed = actualProducts.Keys.Where(k => !declaredProducts.ContainsKey(k)).ToList();
        var changed = declaredProducts.Keys
            .Where(actualProducts.ContainsKey)
            .Where(k => !ProductShapeEquals(declaredProducts[k], actualProducts[k]))
            .ToList();

        var declaredEdges = composed.DeclaredEdges.ToDictionary(e => e.EdgeId, StringComparer.Ordinal);
        var actualEdges = schema.DeclaredEdges.ToDictionary(e => e.EdgeId, StringComparer.Ordinal);

        added.AddRange(declaredEdges.Keys.Where(k => !actualEdges.ContainsKey(k)));
        removed.AddRange(actualEdges.Keys.Where(k => !declaredEdges.ContainsKey(k)));
        changed.AddRange(declaredEdges.Keys
            .Where(actualEdges.ContainsKey)
            .Where(k => !EdgeShapeEquals(declaredEdges[k], actualEdges[k])));

        return new DeclarativeGraphDiff(
            added.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            removed.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            changed.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    /// <summary>端点闭合（C17/DP1）：端点必为已登记产品；data.* 边目标字段须 ⊆ 目标产品出字段。</summary>
    private static void ValidateClosure(
        IReadOnlyList<DataDependencyEdge> edges,
        IReadOnlyDictionary<string, DataProductSpec> products,
        List<DeclarativeGraphDiagnostic> diagnostics)
    {
        foreach (var edge in edges)
        {
            if (!products.TryGetValue(edge.ToProductId, out var target))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D04", $"边 {edge.EdgeId} 的目标产品未登记：{edge.ToProductId}。"));
                continue;
            }

            if (!products.ContainsKey(edge.FromProductId))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D04", $"边 {edge.EdgeId} 的源产品未登记：{edge.FromProductId}。"));
                continue;
            }

            if (!IsDataEdge(edge.EdgeKind)) continue;

            if (edge.ToFields.Count == 0)
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D05", $"边 {edge.EdgeId} 未声明目标字段（字段级依赖须闭合到字段）。"));
                continue;
            }

            var declared = new HashSet<string>(target.OutFields.Select(f => f.Name), StringComparer.Ordinal);
            foreach (var field in edge.ToFields.Where(f => !declared.Contains(f)))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D05", $"越权写：边 {edge.EdgeId} 的目标字段 '{field}' 未在 {target.ProductId} 出字段中声明。"));
            }
        }
    }

    /// <summary>边族声明（DP4：归属/顺序只允许 declared；数据依赖允许 declared + observed）。</summary>
    private static GraphEdgeFamilySpec[] BuildEdgeFamilies() =>
    [
        new(DataEdgeKinds.StructureOwns, [DependencyAssertion.Declared]),
        new(DataEdgeKinds.ControlSequence, [DependencyAssertion.Declared]),
        new(DataEdgeKinds.Derives, [DependencyAssertion.Declared, DependencyAssertion.Observed]),
        new(DataEdgeKinds.Aggregates, [DependencyAssertion.Declared, DependencyAssertion.Observed]),
    ];

    private static bool IsDataEdge(string kind)
        => string.Equals(kind, DataEdgeKinds.Derives, StringComparison.Ordinal)
           || string.Equals(kind, DataEdgeKinds.Aggregates, StringComparison.Ordinal);

    private static IReadOnlyList<FieldSpec> NormalizeFields(
        IReadOnlyList<FieldSpec>? fields, string? sourceId, string productId,
        string section, List<DeclarativeGraphDiagnostic> diagnostics)
    {
        if (fields is null || fields.Count == 0) return [];

        var result = new List<FieldSpec>(fields.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic("D08", $"{productId}.{section} 含空字段名。", sourceId));
                continue;
            }

            if (!FieldNamePattern.IsMatch(field.Name))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D08", $"{productId}.{section} 字段名形态非法：{field.Name}。", sourceId));
                continue;
            }

            if (!seen.Add(field.Name))
            {
                diagnostics.Add(new DeclarativeGraphDiagnostic(
                    "D08", $"{productId}.{section} 字段重名：{field.Name}。", sourceId));
                continue;
            }

            result.Add(field);
        }

        return result;
    }

    private static bool ProductShapeEquals(DataProductSpec a, DataProductSpec b)
        => string.Equals(a.LevelId, b.LevelId, StringComparison.Ordinal)
           && string.Equals(a.Role, b.Role, StringComparison.Ordinal)
           && string.Equals(a.Stage, b.Stage, StringComparison.Ordinal)
           && string.Equals(a.Compatibility, b.Compatibility, StringComparison.Ordinal)
           && string.Equals(a.IdempotencyKey, b.IdempotencyKey, StringComparison.Ordinal)
           && a.Version == b.Version
           && a.OutFields.Select(f => f.Name).SequenceEqual(b.OutFields.Select(f => f.Name), StringComparer.Ordinal)
           && a.InFields.Select(f => f.Name).SequenceEqual(b.InFields.Select(f => f.Name), StringComparer.Ordinal);

    private static bool EdgeShapeEquals(DataDependencyEdge a, DataDependencyEdge b)
        => string.Equals(a.EdgeKind, b.EdgeKind, StringComparison.Ordinal)
           && string.Equals(a.FromProductId, b.FromProductId, StringComparison.Ordinal)
           && string.Equals(a.ToProductId, b.ToProductId, StringComparison.Ordinal)
           && string.Equals(a.Label, b.Label, StringComparison.Ordinal)
           && a.FromFields.SequenceEqual(b.FromFields, StringComparer.Ordinal)
           && a.ToFields.SequenceEqual(b.ToFields, StringComparer.Ordinal)
           && a.Assertion == b.Assertion
           && PropertiesEqual(a.Properties, b.Properties);

    /// <summary>属性集等价（null 与空集等价；逐键逐值 Ordinal 比较）。</summary>
    private static bool PropertiesEqual(
        IReadOnlyDictionary<string, string?>? a, IReadOnlyDictionary<string, string?>? b)
    {
        var leftEmpty = a is null || a.Count == 0;
        var rightEmpty = b is null || b.Count == 0;
        if (leftEmpty || rightEmpty) return leftEmpty && rightEmpty;

        if (a!.Count != b!.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other)) return false;
            if (!string.Equals(value, other, StringComparison.Ordinal)) return false;
        }

        return true;
    }
}

