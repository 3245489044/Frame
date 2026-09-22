namespace Usharp.Semantics;

/// <summary>
/// 引用图子集判据（落图裁决）：<c>StepRef</c>/<c>Invokes</c>/<c>RefersTo</c>/<c>CrRef</c> 与 <c>[Ref]</c> 声明的
/// 自定义边类型**落图**；<c>Over</c>（矩阵→空间）与 <c>RowRef</c>（矩阵行内引用）**不落图**
/// —— 二者属编译面从属关系与运算面，不是运行期依赖，落图会把编译期结构混进影响域分析。
/// </summary>
public static class UsharpEdgeSelection
{
    /// <summary>该边类型是否进入引用图（环检测 / 影响域 / 路径 / 流完整性）。</summary>
    public static bool IsGraphEdge(string? type)
        => !string.IsNullOrWhiteSpace(type)
           && !string.Equals(type, UsharpGraphBuilder.Over, StringComparison.Ordinal)
           && !string.Equals(type, UsharpGraphBuilder.RowRef, StringComparison.Ordinal);

    /// <summary>编译产物中进入引用图的边集（声明序）。</summary>
    public static IReadOnlyList<UsharpEdge> GraphEdges(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return compilation.Edges.Where(e => IsGraphEdge(e.Type)).ToList();
    }

    /// <summary>边端点全集（源在前、目标在后，声明序去重）。</summary>
    public static IReadOnlyList<string> Nodes(IReadOnlyList<UsharpEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<string>();
        foreach (var e in edges)
        {
            if (seen.Add(e.Source)) nodes.Add(e.Source);
            if (seen.Add(e.Target)) nodes.Add(e.Target);
        }
        return nodes;
    }

    /// <summary>邻接取函数（源 → 目标集合，声明序去重）；未出现的源返回空。</summary>
    public static Func<string, IEnumerable<string>> Adjacency(IReadOnlyList<UsharpEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (!map.TryGetValue(e.Source, out var targets))
            {
                targets = new List<string>();
                map[e.Source] = targets;
            }
            if (!targets.Contains(e.Target, StringComparer.Ordinal)) targets.Add(e.Target);
        }

        return node => map.TryGetValue(node ?? string.Empty, out var t) ? t : Array.Empty<string>();
    }
}
