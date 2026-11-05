namespace Modules.Node.Relations;

/// <summary>
/// 边可达性（只读闭包）：按**声明的边类型**收集从起点可达的端点；环安全、顺序不依赖边遍历顺序。
/// <para>用于选择/继承的作用域判定；不解释业务语义、不写库。</para>
/// </summary>
public static class EdgeReachability
{
    /// <summary>
    /// 从 <paramref name="start"/> 出发沿 <paramref name="edges"/> 求可达端点集（含起点）。
    /// </summary>
    /// <param name="start">起点端点。</param>
    /// <param name="edges">全部边。</param>
    /// <param name="kinds">允许的边类型（null/空 = 全部）。</param>
    public static IReadOnlySet<NodeEdgeEndpoint> Reachable(
        NodeEdgeEndpoint start,
        IReadOnlyList<NodeEdge> edges,
        IReadOnlyCollection<string>? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(edges);

        var allowed = kinds is { Count: > 0 }
            ? new HashSet<string>(kinds, StringComparer.Ordinal)
            : null;

        var visited = new HashSet<NodeEdgeEndpoint> { start };
        var pending = new Stack<NodeEdgeEndpoint>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var edge in edges)
            {
                if (edge.To is null)
                {
                    continue;
                }

                if (allowed is not null && !allowed.Contains(edge.Kind))
                {
                    continue;
                }

                if (!edge.From.Equals(current))
                {
                    continue;
                }

                if (visited.Add(edge.To))
                {
                    pending.Push(edge.To);
                }
            }
        }

        return visited;
    }
}
