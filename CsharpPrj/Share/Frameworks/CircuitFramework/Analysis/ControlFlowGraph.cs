using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Analysis;

/// <summary>流程图边类型（R4）：覆盖 Wire / 控制边 / 分支 / 分派 / 区域 / 调用。</summary>
public enum FlowEdgeKind
{
    /// <summary>无条件数据边。</summary>
    Wire,

    /// <summary>控制边（受控 Wire 或 Sequence 镜像）。</summary>
    Control,

    /// <summary>分支 then。</summary>
    BranchThen,

    /// <summary>分支 else。</summary>
    BranchElse,

    /// <summary>分派 route（命中取值）。</summary>
    SwitchRoute,

    /// <summary>分派默认兜底。</summary>
    SwitchDefault,

    /// <summary>区域（SubCircuit）。</summary>
    Region,

    /// <summary>调用（Call）。</summary>
    Call,

    /// <summary>效果依赖（G7）：写共享通道的节点 → 读同通道同主题的节点（Read-after-Write）。</summary>
    Effect,
}

/// <summary>流程图节点（R4）：节点 Id + Kind + 是否为区域/调用承载（后者来自符号声明）。</summary>
public sealed record FlowNode(string Id, string Kind, bool IsRegion, bool IsCall);

/// <summary>流程图边（R4）：有向边 + 分类 + 可选标签（判定键 / 路由取值）。</summary>
public sealed record FlowEdge(string FromNodeId, string ToNodeId, FlowEdgeKind Kind, string? Label = null);

/// <summary>
/// 全流程图（R4）：覆盖分支/区域/调用的完整控制流投影（声明式、可 diff、可离线审阅）。
/// <para>节点/边集合 = 电路声明面（确定性顺序 = 声明序）⇒ 渲染产物对同一声明幂等。</para>
/// </summary>
public sealed class ControlFlowGraph
{
    /// <summary>由电路投影全流程图（纯读投影，不回写电路）。</summary>
    public ControlFlowGraph(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var regionNodes = circuit.Symbols.OfType<SubCircuitSymbol>()
            .Select(s => s.NodeId).ToHashSet(StringComparer.Ordinal);
        var callNodes = circuit.Symbols.OfType<CallSymbol>()
            .Select(s => s.CallerNodeId).ToHashSet(StringComparer.Ordinal);

        Nodes = circuit.Nodes
            .Select(n => new FlowNode(n.Id, n.Kind, regionNodes.Contains(n.Id), callNodes.Contains(n.Id)))
            .ToList();

        var edges = new List<FlowEdge>();
        foreach (var w in circuit.Wires)
        {
            var controlled = !string.IsNullOrWhiteSpace(w.When);
            edges.Add(new FlowEdge(w.From.NodeId, w.To.NodeId,
                controlled ? FlowEdgeKind.Control : FlowEdgeKind.Wire,
                controlled ? w.When : null));
        }

        foreach (var s in circuit.Symbols.OfType<SequenceSymbol>())
            if (s.NodeIds.Count == 2)
                edges.Add(new FlowEdge(s.NodeIds[0], s.NodeIds[1], FlowEdgeKind.Control));

        foreach (var b in circuit.Symbols.OfType<BranchSymbol>())
        {
            edges.Add(new FlowEdge(b.ConditionNodeId, b.ThenNodeId, FlowEdgeKind.BranchThen, b.JudgeKey));
            if (!string.IsNullOrWhiteSpace(b.ElseNodeId))
                edges.Add(new FlowEdge(b.ConditionNodeId, b.ElseNodeId!, FlowEdgeKind.BranchElse, b.JudgeKey));
        }

        foreach (var sw in circuit.Symbols.OfType<SwitchSymbol>())
        {
            foreach (var kv in sw.Routes)
                edges.Add(new FlowEdge(sw.SelectorNodeId, kv.Value, FlowEdgeKind.SwitchRoute, kv.Key));
            if (!string.IsNullOrWhiteSpace(sw.DefaultNodeId))
                edges.Add(new FlowEdge(sw.SelectorNodeId, sw.DefaultNodeId!, FlowEdgeKind.SwitchDefault, sw.SelectorKey));
        }

        foreach (var c in circuit.Symbols.OfType<CallSymbol>())
            edges.Add(new FlowEdge(c.CallerNodeId, c.Target.CanonicalId, FlowEdgeKind.Call, c.Target.CanonicalId));

        foreach (var r in circuit.Symbols.OfType<SubCircuitSymbol>())
            edges.Add(new FlowEdge(r.NodeId, r.Target.CanonicalId, FlowEdgeKind.Region, r.Target.CanonicalId));

        // G7 效果依赖：写节点 → 读同通道同主题节点（Read-after-Write），使值面副作用在图上一等可见。
        var effectWrites = circuit.Symbols.OfType<EffectSymbol>()
            .Where(e => e.Effect == EffectKind.Writes).ToList();
        var effectReads = circuit.Symbols.OfType<EffectSymbol>()
            .Where(e => e.Effect == EffectKind.Reads).ToList();
        foreach (var w in effectWrites)
            foreach (var r in effectReads)
                if (string.Equals(r.Channel, w.Channel, StringComparison.Ordinal)
                    && string.Equals(r.Subject, w.Subject, StringComparison.Ordinal))
                    edges.Add(new FlowEdge(w.NodeId, r.NodeId, FlowEdgeKind.Effect, $"{w.Channel}:{w.Subject}"));

        Edges = edges;
    }

    /// <summary>节点集合（声明序）。</summary>
    public IReadOnlyList<FlowNode> Nodes { get; }

    /// <summary>边集合（声明序：Wire → 控制边 → 分支 → 分派 → 调用 → 区域）。</summary>
    public IReadOnlyList<FlowEdge> Edges { get; }
}
