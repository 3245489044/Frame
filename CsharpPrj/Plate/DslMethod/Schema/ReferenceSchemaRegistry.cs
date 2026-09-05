using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;

namespace Plate.DslMethod.Schema;

/// <summary>
/// 引用 Schema 注册中心（[Node]/[Edge]/[Constraint] 对应关系的逻辑层）。
/// 职责：装载文档 → 建索引 → 提取节点/边/约束 → 校验（悬空边端节点、悬空约束引用、重复、dep 目标）。
/// 复用 <see cref="DslIndex"/> 与 <see cref="ValidationIssue"/>，对齐 <see cref="ContractRegistry"/> 的治理模式。
/// 解决「引用类型词汇表散落」：全部节点/边/约束以 DSL 声明集中登记，可机检、可反向引用、可升级。
/// </summary>
public sealed class ReferenceSchemaRegistry
{
    private readonly DslIndex _index = new();
    private readonly List<DslDocument> _documents = new();
    private readonly List<ResourceNodeType> _nodes = new();
    private readonly List<ReferenceEdgeType> _edges = new();
    private readonly List<ReferenceConstraint> _constraints = new();

    public DslIndex Index => _index;
    public IReadOnlyList<DslDocument> Documents => _documents;
    public IReadOnlyList<ResourceNodeType> Nodes => _nodes;
    public IReadOnlyList<ReferenceEdgeType> Edges => _edges;
    public IReadOnlyList<ReferenceConstraint> Constraints => _constraints;

    /// <summary>装载一组文档。全部文档先入索引，再统一提取（保证后续校验可见全索引）。</summary>
    public void Load(IEnumerable<DslDocument> documents)
    {
        foreach (var d in documents)
        {
            _documents.Add(d);
            _index.AddDocument(d);
        }
        foreach (var d in _documents)
            foreach (var decl in d.Declarations)
            {
                switch (decl.Kind)
                {
                    case DslSymbolKind.Node:
                        var n = ResourceNodeType.From(decl);
                        if (n != null) _nodes.Add(n);
                        break;
                    case DslSymbolKind.Edge:
                        var e = ReferenceEdgeType.From(decl);
                        if (e != null) _edges.Add(e);
                        break;
                    case DslSymbolKind.Constraint:
                        var c = ReferenceConstraint.From(decl);
                        if (c != null) _constraints.Add(c);
                        break;
                }
            }
    }

    /// <summary>编译为可序列化 Schema（运行时消费）。</summary>
    public ReferenceSchema ToSchema()
        => new()
        {
            Nodes = _nodes.ToList(),
            Edges = _edges.ToList(),
            Constraints = _constraints.ToList(),
        };

    /// <summary>
    /// 校验全部声明：重复符号、[Edge].src/tgt 悬空节点类型、[Constraint] 悬空 Edge/节点类型、dep 目标未声明。
    /// 特殊值 <c>any</c>/<c>*</c> 表示不约束（跳过悬空检查）。
    /// </summary>
    public List<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();

        var nodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _nodes)
            if (!nodeNames.Add(n.Name))
                issues.Add(new ValidationIssue(null, $"[Node] 重复定义: {n.Name} ({n.SourceFile}:{n.LineNumber})"));

        var edgeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _edges)
        {
            if (!edgeNames.Add(e.Name))
                issues.Add(new ValidationIssue(null, $"[Edge] 重复定义: {e.Name} ({e.SourceFile}:{e.LineNumber})"));
            if (!StringValueParser.IsAny(e.Source) && !nodeNames.Contains(e.Source!))
                issues.Add(new ValidationIssue(null, $"[Edge] {e.Name}: src 悬空节点类型 '{e.Source}' ({e.SourceFile}:{e.LineNumber})"));
            if (!StringValueParser.IsAny(e.Target) && !nodeNames.Contains(e.Target!))
                issues.Add(new ValidationIssue(null, $"[Edge] {e.Name}: tgt 悬空节点类型 '{e.Target}' ({e.SourceFile}:{e.LineNumber})"));
        }

        // 第二遍：dep 目标须在完整边集合内（避免声明顺序导致误报）
        foreach (var e in _edges)
            if (!string.IsNullOrWhiteSpace(e.ReplacedBy) && !edgeNames.Contains(e.ReplacedBy!))
                issues.Add(new ValidationIssue(null, $"[Edge] {e.Name}: dep 目标 '{e.ReplacedBy}' 未声明 ({e.SourceFile}:{e.LineNumber})"));

        foreach (var c in _constraints)
        {
            if (!edgeNames.Contains(c.Edge))
                issues.Add(new ValidationIssue(null, $"[Constraint] {c.Edge}: 引用未声明的 Edge ({c.SourceFile}:{c.LineNumber})"));
            if (!StringValueParser.IsAny(c.SrcType) && !nodeNames.Contains(c.SrcType))
                issues.Add(new ValidationIssue(null, $"[Constraint] {c.Edge}: srcType 悬空节点类型 '{c.SrcType}' ({c.SourceFile}:{c.LineNumber})"));
            foreach (var t in c.TgtTypes)
                if (!StringValueParser.IsAny(t) && !nodeNames.Contains(t))
                    issues.Add(new ValidationIssue(null, $"[Constraint] {c.Edge}: tgtType 悬空节点类型 '{t}' ({c.SourceFile}:{c.LineNumber})"));
        }

        return issues;
    }

}
