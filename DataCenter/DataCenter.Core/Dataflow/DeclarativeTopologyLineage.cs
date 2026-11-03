using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 拓扑 → 数据依赖边生成器（F5 唯一实现）：按声明面属性 <c>derives</c> 生成字段级 <c>data.derives</c> 边。
/// <para>
/// 纪律：① 纯函数（同 <see cref="DeclaredGraph"/> 两次投影结果一致，无时间/随机/IO）；
/// ② 规则只来自声明面（<see cref="TopologyLineageKeys.Derives"/>），框架不认识任何具体关系名；
/// ③ 未映射/非法项一律进诊断（不默认放行，也不产出近似边）；
/// ④ 输出按 <c>EdgeId</c> Ordinal 升序（可逐字复算）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-可追踪数据流图框架-设计与实施.md §3.5。
/// </summary>
public sealed class DeclarativeTopologyLineage : ITopologyLineage
{
    /// <summary>诊断码前缀（门禁/单测按码断言）。</summary>
    public static class Diagnostics
    {
        /// <summary>端点产品未在声明图登记。</summary>
        public const string EndpointUndeclared = "L01";

        /// <summary>字段对形态非法（缺分隔符/空字段）。</summary>
        public const string MalformedPair = "L02";

        /// <summary>derives 属性为空（声明了但无可生成项）。</summary>
        public const string EmptyDerives = "L03";

        /// <summary>生成边键重复。</summary>
        public const string DuplicateEdge = "L04";
    }

    /// <inheritdoc />
    public TopologyLineageResult Project(DeclaredGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var products = new HashSet<string>(
            graph.Products.Where(p => p is not null).Select(p => p.ProductId), StringComparer.Ordinal);
        var edges = new List<DataDependencyEdge>();
        var diagnostics = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaration in graph.Edges.Where(e => e is not null))
        {
            if (declaration.Properties is null
                || !declaration.Properties.TryGetValue(TopologyLineageKeys.Derives, out var raw))
                continue;

            var edgeKey = DeclarativeGraphComposer.EdgeIdOf(declaration);
            if (!products.Contains(declaration.FromProductId) || !products.Contains(declaration.ToProductId))
            {
                diagnostics.Add($"{Diagnostics.EndpointUndeclared}: {edgeKey} 端点产品未登记");
                continue;
            }

            var fromFields = new List<string>();
            var toFields = new List<string>();
            foreach (var pair in (raw ?? string.Empty).Split(TopologyLineageKeys.PairSeparator))
            {
                var entry = pair.Trim();
                if (entry.Length == 0) continue;

                var separator = entry.IndexOf(TopologyLineageKeys.FieldSeparator, StringComparison.Ordinal);
                if (separator <= 0 || separator + TopologyLineageKeys.FieldSeparator.Length >= entry.Length)
                {
                    diagnostics.Add($"{Diagnostics.MalformedPair}: {edgeKey} 字段对 '{entry}' 缺 {TopologyLineageKeys.FieldSeparator}");
                    continue;
                }

                var from = entry[..separator].Trim();
                var to = entry[(separator + TopologyLineageKeys.FieldSeparator.Length)..].Trim();
                if (from.Length == 0 || to.Length == 0)
                {
                    diagnostics.Add($"{Diagnostics.MalformedPair}: {edgeKey} 字段对 '{entry}' 含空字段");
                    continue;
                }

                fromFields.Add(from);
                toFields.Add(to);
            }

            if (fromFields.Count == 0)
            {
                diagnostics.Add($"{Diagnostics.EmptyDerives}: {edgeKey} 无可生成的字段对");
                continue;
            }

            var edgeId = DataEdgeIdOf(declaration);
            if (!seen.Add(edgeId))
            {
                diagnostics.Add($"{Diagnostics.DuplicateEdge}: {edgeId} 生成边键重复（已跳过）");
                continue;
            }

            edges.Add(new DataDependencyEdge(
                edgeId,
                declaration.FromProductId,
                fromFields,
                declaration.ToProductId,
                toFields,
                DataEdgeKinds.Derives,
                DependencyAssertion.Declared,
                string.IsNullOrWhiteSpace(declaration.Label) ? null : declaration.Label.Trim()));
        }

        return new TopologyLineageResult(
            edges.OrderBy(e => e.EdgeId, StringComparer.Ordinal).ToArray(),
            diagnostics.OrderBy(d => d, StringComparer.Ordinal).ToArray());
    }

    /// <summary>生成边的规范键（与声明边键同形态：<c>{from}|{kind}|{to}[|{label}]</c>；复用合成器格式）。</summary>
    public static string DataEdgeIdOf(EdgeDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return DeclarativeGraphComposer.EdgeIdOf(new EdgeDeclaration(
            declaration.FromProductId,
            declaration.ToProductId,
            DataEdgeKinds.Derives,
            [],
            [],
            DependencyAssertion.Declared,
            declaration.Label));
    }
}
