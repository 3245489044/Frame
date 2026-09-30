using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Analysis;

/// <summary>一条调用链边：调用者节点 → 被调电路引用。</summary>
public sealed record CallEdge(string CallerNodeId, CalleeRef Target);

/// <summary>
/// 调用链图：使"整个调用图 = 节点间有向边集合"，天然可遍历、可做影响面分析（"谁调用了 X"）。
/// 覆盖 Call 与 SubCircuit 两种产生调用边的结构符号。
/// </summary>
public sealed class CircuitCallGraph
{
    private readonly Circuit _circuit;
    private readonly IReadOnlyList<CallEdge> _edges;

    public CircuitCallGraph(Circuit circuit)
    {
        _circuit = circuit;
        _edges = CollectEdges(circuit);
    }

    /// <summary>调用链边集合（一等数据结构，可遍历/静态分析/可视化）。</summary>
    public IReadOnlyList<CallEdge> Edges => _edges;

    /// <summary>本电路直接引用的所有子电路（去重，按 CanonicalId）。</summary>
    public IReadOnlyList<CalleeRef> Callees =>
        _edges.Select(e => e.Target).DistinctBy(t => t.CanonicalId).ToList();

    /// <summary>在本电路内直接调用指定目标电路的节点 id 集合。</summary>
    public IEnumerable<string> GetDirectCallers(string canonicalTargetId) =>
        _edges.Where(e => e.Target.CanonicalId == canonicalTargetId)
              .Select(e => e.CallerNodeId);
    
    /// <summary>本电路是否（直接或经其它子电路）调用了指定目标电路。</summary>
    public bool CallsInto(string canonicalTargetId) =>
        _edges.Any(e => e.Target.CanonicalId == canonicalTargetId);

    /// <summary>
    /// 影响面分析："谁（的电路）依赖了 X"。返回被直接影响命中的电路 Id 集合（含调用 X 的直接调用者与上游传递）。
    /// registry 提供跨电路解析；缺省仅在本电路内判定。
    /// </summary>
    public ISet<string> GetImpactedBy(
        string canonicalTargetId,
        IReadOnlyDictionary<string, Circuit>? registry = null)
    {
        var byCallerCircuit = BuildReverse(registry);
        var affected = new HashSet<string>();
        var pending = new Queue<string>();
        foreach (var c in DirectConsumerCircuits(canonicalTargetId))
            pending.Enqueue(c);

        while (pending.Count > 0)
        {
            var cid = pending.Dequeue();
            if (!affected.Add(cid)) continue;

            if (byCallerCircuit.TryGetValue(cid, out var callers))
                foreach (var upstream in callers)
                    pending.Enqueue(upstream);
        }
        affected.Remove(canonicalTargetId);
        return affected;
    }

    // 直接消费目标电路的电路Id（本电路 + registry 中直接调用目标者）
    private IEnumerable<string> DirectConsumerCircuits(string canonicalTargetId)
    {
        var set = new HashSet<string>();
        if (_circuit is not null && CallsInto(canonicalTargetId))
            set.Add(_circuit.Id);
        return set;
    }

    // circuitId -> 直接调用它的上游电路Id集合（registry 解析）
    private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildReverse(
        IReadOnlyDictionary<string, Circuit>? registry)
    {
        var map = new Dictionary<string, List<string>>();
        Action<Circuit> scan = c =>
        {
            foreach (var e in CollectEdges(c))
            {
                var down = e.Target.CanonicalId;
                if (!map.TryGetValue(down, out var list))
                    map[down] = list = new List<string>();
                list.Add(c.Id);
            }
        };
        scan(_circuit);
        if (registry is not null)
            foreach (var kv in registry)
                scan(kv.Value);
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    private static IReadOnlyList<CallEdge> CollectEdges(Circuit circuit) =>
        circuit.Symbols.OfType<CallSymbol>()
                        .Select(s => new CallEdge(s.CallerNodeId, s.Target))
                        .Concat(circuit.Symbols.OfType<SubCircuitSymbol>()
                                            .Select(s => new CallEdge(s.NodeId, s.Target)))
                        .ToList();
}