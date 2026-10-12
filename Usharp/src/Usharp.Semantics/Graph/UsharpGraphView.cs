namespace Usharp.Semantics;

/// <summary>
/// 编译产物的**图视图**：落图边子集 → 节点序 + 邻接取函数（构建一次、多处复用）。
/// <para>
/// 消费方：<see cref="UsharpGraphService"/> 的环检测 / 可达 / 路径查询 —— 同一编译的三处查询共用同一视图，
/// 不再各自重建边集与邻接表（D8 收敛点）。
/// </para>
/// </summary>
public sealed class UsharpGraphView
{
    private UsharpGraphView(
        IReadOnlyList<UsharpEdge> edges,
        IReadOnlyList<string> nodes,
        Func<string, IEnumerable<string>> adjacency)
    {
        Edges = edges;
        Nodes = nodes;
        Adjacency = adjacency;
    }

    /// <summary>落图边（声明序；<c>Over</c>/<c>RowRef</c> 已排除）。</summary>
    public IReadOnlyList<UsharpEdge> Edges { get; }

    /// <summary>边端点全集（源在前、声明序去重）。</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>邻接取函数（源 → 目标集，声明序去重）。</summary>
    public Func<string, IEnumerable<string>> Adjacency { get; }

    /// <summary>构建视图（一次枚举边集即得三件产物）。</summary>
    public static UsharpGraphView Build(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var edges = UsharpEdgeSelection.GraphEdges(compilation);
        return new UsharpGraphView(edges, UsharpEdgeSelection.Nodes(edges), UsharpEdgeSelection.Adjacency(edges));
    }
}
