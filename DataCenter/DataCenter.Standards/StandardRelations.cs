namespace DataCenter.Standards;

/// <summary>
/// 关系边类型（L3 关系图，A5 并入 F2-b）：边一律由**在册单源**派生（词表登记表 / 结构门绑定 / 标准模型），
/// 守卫不得另抄实体表（同一事实只有一处判）。
/// </summary>
public enum RelationKind
{
    /// <summary>族 → 词（词表登记，① 落点）。</summary>
    FamilyWord,

    /// <summary>词 → 结构门节点（② 落点）。</summary>
    WordSchema,

    /// <summary>词 → 中立模型成员（④ 落点）。</summary>
    WordModel,

    /// <summary>词 → 读取器分派面（③ 落点）。</summary>
    WordReader,

    /// <summary>词 → 实现证据（⑤ 外来证据）。</summary>
    WordImplementation,

    /// <summary>词 → 自证 / 对拍证据（⑥ 外来证据）。</summary>
    WordAttestation,
}

/// <summary>关系边（<paramref name="From"/> → <paramref name="To"/>；<paramref name="Detail"/> 承载悬空等诊断）。</summary>
public sealed record RelationEdge(string From, string To, RelationKind Kind, string? Detail = null);

/// <summary>
/// L3 关系图（只读）：把六向完备性矩阵的落点关系显式成边，提供 4 类查询 ——
/// <list type="bullet">
/// <item><see cref="Outgoing"/> / <see cref="Incoming"/>：单跳关系（「这个词落在哪」/「谁引用了它」）；</item>
/// <item><see cref="Impact"/>：影响分析（改一个词 / 族 → 波及的全部落点，传递闭包）；</item>
/// <item><see cref="Dangling"/>：悬空引用（结构门指针指向不存在的节点）。</item>
/// </list>
/// <para>节点 id 形态：<c>family:&lt;族&gt;</c> / <c>word:&lt;族&gt;/&lt;词&gt;</c> / <c>schema:&lt;文件&gt;#&lt;指针&gt;</c> /
/// <c>model:&lt;类型&gt;.&lt;成员&gt;</c> / <c>reader:&lt;族&gt;</c> / <c>impl|attest:&lt;族&gt;/&lt;词&gt;</c>（不透明字符串，仅 Ordinal 比较）。</para>
/// </summary>
public sealed class RelationGraph
{
    private readonly IReadOnlyList<RelationEdge> _edges;
    private readonly IReadOnlySet<string> _existingSchemaNodes;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> _outgoing;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> _incoming;

    internal RelationGraph(
        IReadOnlyList<RelationEdge> edges,
        IReadOnlySet<string> existingSchemaNodes,
        IReadOnlySet<string> boundSchemaNodes)
    {
        _edges = edges;
        _existingSchemaNodes = existingSchemaNodes;
        BoundSchemaNodes = boundSchemaNodes;
        _outgoing = Index(edges, edge => edge.From);
        _incoming = Index(edges, edge => edge.To);
        Nodes = edges.SelectMany(edge => new[] { edge.From, edge.To })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(node => node, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>全部边（登记序 = 家族登记序，确定性）。</summary>
    public IReadOnlyList<RelationEdge> Edges => _edges;

    /// <summary>全部节点（Ordinal 升序，去重）。</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>有词表派生绑定的结构门节点（判定源 = <see cref="SchemaEnumSynchronizer"/>；防「结构门手写漂移」）。</summary>
    public IReadOnlySet<string> BoundSchemaNodes { get; }

    /// <summary>给定节点的出边（登记序）。</summary>
    public IReadOnlyList<RelationEdge> Outgoing(string node)
        => _outgoing.TryGetValue(node, out var edges) ? edges : Array.Empty<RelationEdge>();

    /// <summary>给定节点的入边（登记序）。</summary>
    public IReadOnlyList<RelationEdge> Incoming(string node)
        => _incoming.TryGetValue(node, out var edges) ? edges : Array.Empty<RelationEdge>();

    /// <summary>悬空引用：结构门指针在**内嵌单源**中不存在的边（空 = 无悬空）。</summary>
    public IReadOnlyList<RelationEdge> Dangling => _edges
        .Where(edge => edge.Kind == RelationKind.WordSchema && !_existingSchemaNodes.Contains(edge.To))
        .ToArray();

    /// <summary>
    /// 影响分析（改 <paramref name="node"/> 波及谁）：沿出边求传递闭包（不含起点；Ordinal 升序，去重）。
    /// 未知节点 → 空集（查询面不承担校验职责，不抛）。
    /// </summary>
    public IReadOnlyList<string> Impact(string node)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(node);

        while (queue.Count > 0)
            foreach (var edge in Outgoing(queue.Dequeue()))
                if (reached.Add(edge.To))
                    queue.Enqueue(edge.To);

        reached.Remove(node);
        return reached.OrderBy(item => item, StringComparer.Ordinal).ToArray();
    }

    /// <summary>文本视图（诊断 / 基线条目用）。</summary>
    public string Render()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var edge in _edges)
            builder.AppendLine($"{edge.From} -[{edge.Kind}]-> {edge.To}"
                               + (edge.Detail is null ? string.Empty : $"  // {edge.Detail}"));

        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> Index(
        IReadOnlyList<RelationEdge> edges, Func<RelationEdge, string> key)
        => edges.GroupBy(key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RelationEdge>)group.ToArray(), StringComparer.Ordinal);
}

/// <summary>
/// L3 关系图构建器（F2-b 升级：六向矩阵门 + 关系图查询）：节点 / 边全部由在册单源派生 ——
/// <see cref="StandardCompleteness.Landings"/>（家族登记表投影）、<see cref="StandardCompleteness.SchemaNode"/>（结构门探针）、
/// <see cref="SchemaEnumSynchronizer"/>（词表派生绑定）；本件**零领域词汇、零磁盘写**。
/// </summary>
public static class StandardRelations
{
    /// <summary>构建关系图（只读）；<paramref name="evidence"/> 非空时追加 ⑤/⑥ 外来证据边。</summary>
    public static RelationGraph Build(StandardCompletenessEvidence? evidence = null)
    {
        var edges = new List<RelationEdge>();
        var existing = new HashSet<string>(StringComparer.Ordinal);

        foreach (var landing in StandardCompleteness.Landings)
        {
            var word = WordId(landing.Family, landing.Word);
            edges.Add(new RelationEdge(FamilyId(landing.Family), word, RelationKind.FamilyWord));

            if (landing.SchemaPointer is { } pointer)
            {
                var target = SchemaId(landing.SchemaFile, pointer);
                var (exists, _) = StandardCompleteness.SchemaNode(landing.SchemaFile, pointer);
                if (exists) existing.Add(target);

                edges.Add(new RelationEdge(word, target, RelationKind.WordSchema,
                    exists ? null : $"悬空：结构门无该节点（{landing.SchemaFile}#{pointer}）"));
            }

            if (landing.ModelType is { } type && landing.ModelMember is { } member)
                edges.Add(new RelationEdge(word, $"model:{type}.{member}", RelationKind.WordModel));

            if (landing.HasReaderProbe)
                edges.Add(new RelationEdge(word, $"reader:{landing.Family}", RelationKind.WordReader));

            if (evidence is null) continue;

            var key = StandardCompletenessEvidence.Key(landing.Family, landing.Word);
            if (evidence.Implemented.Contains(key))
                edges.Add(new RelationEdge(word, "impl:" + key, RelationKind.WordImplementation));
            if (evidence.Attested.Contains(key))
                edges.Add(new RelationEdge(word, "attest:" + key, RelationKind.WordAttestation));
        }

        return new RelationGraph(edges, existing, BoundNodes());
    }

    /// <summary>悬空判定（单点实现）：结构门指针在内嵌单源中不存在即悬空。</summary>
    public static bool IsDanglingSchema(string schemaFile, string pointer)
        => !StandardCompleteness.SchemaNode(schemaFile, pointer).Exists;

    /// <summary>有词表派生绑定的结构门节点（判定源唯一 = <see cref="SchemaEnumSynchronizer"/>）。</summary>
    private static IReadOnlySet<string> BoundNodes()
    {
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in SchemaEnumSynchronizer.EnumBindings)
            bound.Add(SchemaId(binding.SchemaFile, binding.Pointer));
        foreach (var binding in SchemaEnumSynchronizer.PropertiesKeyBindings)
            bound.Add(SchemaId(binding.SchemaFile, binding.Pointer));

        return bound;
    }

    private static string FamilyId(string family) => "family:" + family;

    private static string WordId(string family, string word) => $"word:{family}/{word}";

    private static string SchemaId(string schemaFile, string pointer) => $"schema:{schemaFile}#{pointer}";
}

