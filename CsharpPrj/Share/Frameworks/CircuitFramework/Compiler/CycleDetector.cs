using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 环检测：图上的环只允许由 Recursion 符号产生，其余任何边不得成环。
/// 对 Wire 图（数据流依赖）求简单环集合。Recursion 的"回边"是符号声明的，不落成 Wire，因此 Wire 图环即为非法环。
/// </summary>
public static class CycleDetector
{
    /// <summary>返回 Wire 上构成的有向环，每个环是一组节点 id（沿依赖顺序排列，首尾相接）。</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindCycles(Circuit circuit) =>
        FindCycles(BuildAdjacency(circuit));

    private static Dictionary<string, List<string>> BuildAdjacency(Circuit circuit)
    {
        var adj = new Dictionary<string, List<string>>();
        void Ensure(string key)
        {
            if (!adj.ContainsKey(key)) adj[key] = new List<string>();
        }
        foreach (var n in circuit.Nodes) Ensure(n.Id);
        foreach (var w in circuit.Wires)
        {
            Ensure(w.From.NodeId);
            Ensure(w.To.NodeId);
            adj[w.From.NodeId].Add(w.To.NodeId);
        }
        return adj;
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(Dictionary<string, List<string>> adj)
    {
        var cycles = new List<IReadOnlyList<string>>();
        var color = new Dictionary<string, int>(); // 0=white,1=gray,2=black
        var stack = new List<string>();

        foreach (var node in adj.Keys) color[node] = 0;

        void Dfs(string u)
        {
            color[u] = 1;
            stack.Add(u);
            foreach (var v in adj[u])
            {
                var c = color.GetValueOrDefault(v, 0);
                if (c == 1)
                {
                    // 找到环：从 v 到 u 的栈片段
                    var idx = stack.IndexOf(v);
                    if (idx >= 0)
                    {
                        var cycle = stack.Skip(idx).Concat(new[] { v }).ToList();
                        cycles.Add(Normalize(cycle));
                    }
                }
                else if (c == 0)
                {
                    Dfs(v);
                }
            }
            stack.RemoveAt(stack.Count - 1);
            color[u] = 2;
        }

        foreach (var node in adj.Keys.Where(k => color[k] == 0))
        {
            Dfs(node);
        }

        return Dedupe(cycles);
    }

    private static List<string> Normalize(List<string> cycle)
    {
        var min = cycle.IndexOf(cycle.Min()!);
        return cycle.Skip(min)
                    .Concat(cycle.Take(min))
                    .Skip(1) // 去掉重复的收尾=v（只保留一次）
                    .Append(cycle.Min()!) // 尾接回最小节点形成闭环的起点重复，便于阅读
                    .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<string>> Dedupe(List<IReadOnlyList<string>> cycles)
    {
        var seen = new HashSet<string>();
        var result = new List<IReadOnlyList<string>>();
        foreach (var c in cycles)
        {
            var key = string.Join(">", c);
            if (seen.Add(key)) result.Add(c);
        }
        return result;
    }
}