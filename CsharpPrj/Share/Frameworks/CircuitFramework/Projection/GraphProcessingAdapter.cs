using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using GraphFoundation.Topology;
using Modules.GraphProcessing.Algorithms;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Projection;

/// <summary>
/// 图算法适配层（读侧）：把电路的 Node/Port/Wire 投影成 GraphProcessing / GraphFoundation 泛型算法
/// 可消费的邻接函数与只读图对象，从而复用现成图算法、删除手写重复实现。
/// <list type="bullet">
/// <item><see cref="FindCycles"/> → <c>Modules.GraphProcessing.Algorithms.DefaultCycleDetector&lt;string&gt;</c></item>
/// <item><see cref="TopologicalSort"/> → <c>GraphFoundation.Topology.GraphTopology.TopologicalSort</c>（装配只读图）</item>
/// <item><see cref="GetReachableNodes"/> → <c>Modules.GraphProcessing.Algorithms.DefaultPathFinder&lt;string&gt;</c></item>
/// </list>
/// 适配层只读电路、产出投影；不反向改业务代码（与 <see cref="UniversalReferenceProjector"/> 同级的读侧风格）。
/// </summary>
public static class GraphProcessingAdapter
{
    /// <summary>按节点 id 把 Wire 依赖投影为邻接函数（From.NodeId → To.NodeId 集合）。</summary>
    public static Func<string, IEnumerable<string>> WireAdjacency(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        var adj = new Dictionary<string, List<string>>();
        foreach (var n in circuit.Nodes) adj[n.Id] = new List<string>();
        foreach (var w in circuit.Wires)
        {
            if (!adj.TryGetValue(w.From.NodeId, out var list))
                adj[w.From.NodeId] = list = new List<string>();
            list.Add(w.To.NodeId);
        }
        return key => adj.TryGetValue(key, out var list) ? list : Enumerable.Empty<string>();
    }

    /// <summary>环检测：委托 DefaultCycleDetector&lt;string&gt;，Wire 图上构成的所有有向环。</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindCycles(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        var nodes = circuit.Nodes.Select(n => n.Id).ToList();
        return new DefaultCycleDetector<string>().FindCycles(nodes, WireAdjacency(circuit));
    }

    /// <summary>拓扑排序：装配只读图后委托 GraphTopology.TopologicalSort；成环返回 null。</summary>
    public static TopologyResult<string>? TopologicalSort(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        using var graph = BuildReadOnlyGraph(circuit);
        return GraphTopology.TopologicalSort(graph);
    }

    /// <summary>从指定节点出发可达的全部节点（含自身），委托 DefaultPathFinder&lt;string&gt;。</summary>
    public static IReadOnlySet<string> GetReachableNodes(Circuit circuit, string start)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        return new DefaultPathFinder<string>().GetReachableNodes(start, WireAdjacency(circuit));
    }

    /// <summary>判断 source 到 target 是否存在路径，委托 DefaultPathFinder&lt;string&gt;。</summary>
    public static bool HasPath(Circuit circuit, string source, string target)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        return new DefaultPathFinder<string>().HasPath(source, target, WireAdjacency(circuit));
    }

    /// <summary>
    /// 按引用关系把 <see cref="ReferenceSymbol"/> 投影为邻接函数（<c>Source → Target</c> 资源/形态标识集合）。
    /// <para>与 <see cref="WireAdjacency"/> 同构，但对象域 = 引用符号（形状/资源标识）而非节点 id；
    /// 供「边 = 与节点无关的静态引用」类图（如形状转换图）做多跳路径规划。</para>
    /// </summary>
    public static Func<string, IEnumerable<string>> ReferenceAdjacency(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        var adj = new Dictionary<string, List<string>>();
        foreach (var symbol in circuit.Symbols.OfType<ReferenceSymbol>())
        {
            if (!adj.TryGetValue(symbol.Source, out var list))
                adj[symbol.Source] = list = new List<string>();
            list.Add(symbol.Target);
        }
        return key => adj.TryGetValue(key, out var list) ? list : Enumerable.Empty<string>();
    }

    /// <summary>
    /// 引用图上 <paramref name="start"/> → <paramref name="end"/> 的最短路径（BFS，含两端）；
    /// <c>start==end</c> ⇒ 单元素；不可达 ⇒ 空。委托 <c>DefaultShortestPathFinder&lt;string&gt;</c>。
    /// </summary>
    public static IReadOnlyList<string> FindShortestReferencePath(Circuit circuit, string start, string end)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        return new DefaultShortestPathFinder<string>().FindShortestPath(start, end, ReferenceAdjacency(circuit));
    }

    /// <summary>把电路装配为只读图（节点=CircuitNode，边=CircuitWire）。</summary>
    public static ICoreGraph<string, CircuitNode, CircuitWire> BuildReadOnlyGraph(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        var graph = GraphFrame.Create<string, CircuitNode, CircuitWire>();
        foreach (var n in circuit.Nodes)
            graph.AddNode(n.Id, n);
        for (var i = 0; i < circuit.Wires.Count; i++)
        {
            var w = circuit.Wires[i];
            graph.AddEdge($"{w.From.NodeId}->{w.To.NodeId}#{i}", w.From.NodeId, w.To.NodeId, true, w, 1.0);
        }
        return graph;
    }
}