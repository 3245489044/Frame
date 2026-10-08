namespace BasicMethod.Declarative.Ledger;

/// <summary>关系边：两端节点 id **不透明**（仅 Ordinal 比较）；<see cref="Kind"/> 由消费方定义；<see cref="Detail"/> 承载悬空等诊断。</summary>
public sealed record LedgerEdge(string From, string To, string Kind, string? Detail = null);

/// <summary>
/// 只读关系图：出 / 入边索引 + 传递闭包（影响分析）+ 悬空边。零领域词：
/// 节点 id 与边类型全部由消费方注入（框架只承担索引与图算法）。
/// </summary>
public sealed class LedgerRelationGraph
{
    private readonly IReadOnlyList<LedgerEdge> _edges;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LedgerEdge>> _outgoing;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LedgerEdge>> _incoming;

    /// <param name="edges">边集（登记序；<c>From</c>/<c>To</c> 须非空）。</param>
    public LedgerRelationGraph(IEnumerable<LedgerEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var list = new List<LedgerEdge>();
        foreach (var edge in edges)
        {
            ArgumentNullException.ThrowIfNull(edge);
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.From);
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.To);
            list.Add(edge);
        }

        _edges = list;
        _outgoing = Index(list, edge => edge.From);
        _incoming = Index(list, edge => edge.To);
        Nodes = list.SelectMany(edge => new[] { edge.From, edge.To })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(node => node, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>全部边（登记序）。</summary>
    public IReadOnlyList<LedgerEdge> Edges => _edges;

    /// <summary>全部节点（Ordinal 升序，去重）。</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>给定节点的出边（登记序）。</summary>
    public IReadOnlyList<LedgerEdge> Outgoing(string node)
        => _outgoing.TryGetValue(node, out var edges) ? edges : [];

    /// <summary>给定节点的入边（登记序）。</summary>
    public IReadOnlyList<LedgerEdge> Incoming(string node)
        => _incoming.TryGetValue(node, out var edges) ? edges : [];

    /// <summary>影响分析：自 <paramref name="node"/> 沿出边可达的全部节点（传递闭包；不含起点自身）。</summary>
    public IReadOnlyList<string> Impact(string node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var reached = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { node };
        var queue = new Queue<string>();
        queue.Enqueue(node);
        while (queue.Count > 0)
            foreach (var edge in Outgoing(queue.Dequeue()))
                if (seen.Add(edge.To))
                {
                    reached.Add(edge.To);
                    queue.Enqueue(edge.To);
                }

        return reached;
    }

    /// <summary>悬空边（<see cref="LedgerEdge.Detail"/> 非空 = 消费方标注的悬空 / 异常）。</summary>
    public IReadOnlyList<LedgerEdge> Dangling()
        => [.. _edges.Where(edge => !string.IsNullOrEmpty(edge.Detail))];

    private static IReadOnlyDictionary<string, IReadOnlyList<LedgerEdge>> Index(
        IReadOnlyList<LedgerEdge> edges, Func<LedgerEdge, string> selector)
    {
        var buckets = new Dictionary<string, List<LedgerEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var key = selector(edge);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets[key] = bucket;
            }

            bucket.Add(edge);
        }

        return buckets.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<LedgerEdge>)pair.Value, StringComparer.Ordinal);
    }
}

/// <summary>
/// 跨台账引用判据（子集 / 版本单调）：违例文案由消费方注入（域文案留消费方）。
/// </summary>
public static class LedgerReferenceRule
{
    /// <summary>
    /// 子集判据：<paramref name="sources"/> ⊆ <paramref name="universe"/>（保持出现序、去重后逐项报告缺失引用）。
    /// </summary>
    public static void EnsureSubset(
        IEnumerable<string> sources,
        IReadOnlySet<string> universe,
        Func<string, string> messageFactory,
        List<string> violations)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(messageFactory);
        ArgumentNullException.ThrowIfNull(violations);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null || universe.Contains(source) || !seen.Add(source)) continue;

            violations.Add(messageFactory(source));
        }
    }

    /// <summary>
    /// 版本单调判据：同键 version 只增不减（与上一快照比对；<paramref name="previous"/> 为 null ⇒ 跳过）。
    /// </summary>
    public static void EnsureMonotonic(
        IEnumerable<(string Key, int Version)> current,
        IReadOnlyDictionary<string, int>? previous,
        Func<string, int, int, string> messageFactory,
        List<string> violations)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(messageFactory);
        ArgumentNullException.ThrowIfNull(violations);

        if (previous is null) return;

        foreach (var (key, version) in current)
            if (previous.TryGetValue(key, out var last) && version < last)
                violations.Add(messageFactory(key, version, last));
    }
}
