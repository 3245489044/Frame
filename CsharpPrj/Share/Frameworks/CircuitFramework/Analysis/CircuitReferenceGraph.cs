using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Analysis;

/// <summary>一条引用关系边：源 → 目标 + 关系语义 + 承载节点（可空）。</summary>
public sealed record ReferenceEdge(string Source, string Target, ReferenceRelation Relation, string? AnchorNodeId = null);

/// <summary>
/// 引用关系图（G5）：把电路上的引用族符号（<see cref="ReferenceSymbol"/>：Owns/Depends/Provides/Consumes）
/// 与结构调用符号（<see cref="CallSymbol"/> / <see cref="SubCircuitSymbol"/>）统一为一张有向图，
/// 提供「谁依赖谁 / 谁调用了 X」的影响面分析（含非 Call 依赖）。
/// <para>与 <see cref="CircuitCallGraph"/> 分工：CallGraph 只收 Call/SubCircuit（调用轴）；本图收全引用轴。</para>
/// </summary>
public sealed class CircuitReferenceGraph
{
    private readonly IReadOnlyList<ReferenceEdge> _edges;
    private readonly Dictionary<string, IReadOnlyList<string>> _reverse;

    public CircuitReferenceGraph(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var edges = new List<ReferenceEdge>();
        foreach (var s in circuit.Symbols.OfType<ReferenceSymbol>())
            edges.Add(new ReferenceEdge(s.Source, s.Target, s.Relation));
        foreach (var c in circuit.Symbols.OfType<CallSymbol>())
            edges.Add(new ReferenceEdge(c.CallerNodeId, c.Target.CanonicalId, ReferenceRelation.Calls, c.CallerNodeId));
        foreach (var r in circuit.Symbols.OfType<SubCircuitSymbol>())
            edges.Add(new ReferenceEdge(r.NodeId, r.Target.CanonicalId, ReferenceRelation.Owns, r.NodeId));

        _edges = edges;
        _reverse = edges
            .GroupBy(e => e.Target, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(e => e.Source).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>全部引用边（声明序：引用符号 → 调用 → 子电路）。</summary>
    public IReadOnlyList<ReferenceEdge> Edges => _edges;

    /// <summary>直接引用指定目标的源集合。</summary>
    public IReadOnlyList<string> GetDirectSources(string target) =>
        _reverse.TryGetValue(target, out var list) ? list : Array.Empty<string>();

    /// <summary>
    /// 影响面分析：谁（沿引用边反向）依赖了 <paramref name="target"/>（含传递上游）；不含 target 自身。
    /// </summary>
    public ISet<string> GetImpactedBy(string target)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        foreach (var s in GetDirectSources(target)) stack.Push(s);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!affected.Add(cur)) continue;
            foreach (var up in GetDirectSources(cur)) stack.Push(up);
        }

        affected.Remove(target);
        return affected;
    }
}
