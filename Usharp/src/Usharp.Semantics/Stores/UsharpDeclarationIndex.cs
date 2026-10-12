using Plate.DslMethod;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 声明索引（按 <see cref="DslSymbolKind"/> 分组，**一次遍历**）：把「扫描全部声明找 <c>[Node]</c>/<c>[Edge]</c>/…」
/// 的多处重复实现收敛为单点，并提供 <c>[Edge] ur=</c> 的单一读数（落库口径与映射闸A 同源）。
/// <para>由 <see cref="UsharpCompilation.Declarations"/> 每次编译构建一次，规则/发射/落库共用同一实例。</para>
/// </summary>
public sealed class UsharpDeclarationIndex
{
    private readonly IReadOnlyList<DslDeclaration> _all;
    private readonly IReadOnlyDictionary<DslSymbolKind, IReadOnlyList<DslDeclaration>> _byKind;
    private readonly IReadOnlyDictionary<string, DslDeclaration> _edgesByType;
    private readonly HashSet<string> _nodeNames;

    private UsharpDeclarationIndex(
        IReadOnlyList<DslDeclaration> all,
        IReadOnlyDictionary<DslSymbolKind, IReadOnlyList<DslDeclaration>> byKind,
        IReadOnlyDictionary<string, DslDeclaration> edgesByType,
        HashSet<string> nodeNames)
    {
        _all = all;
        _byKind = byKind;
        _edgesByType = edgesByType;
        _nodeNames = nodeNames;
    }

    /// <summary>一次遍历全部文档声明，按 Kind 分组（保声明序）。</summary>
    public static UsharpDeclarationIndex Build(IEnumerable<DslDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var all = new List<DslDeclaration>();
        var byKind = new Dictionary<DslSymbolKind, List<DslDeclaration>>();
        var edgesByType = new Dictionary<string, DslDeclaration>(StringComparer.Ordinal);
        var nodeNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var decl in documents.SelectMany(d => d.Declarations))
        {
            all.Add(decl);
            if (!byKind.TryGetValue(decl.Kind, out var list)) byKind[decl.Kind] = list = new List<DslDeclaration>();
            list.Add(decl);

            switch (decl.Kind)
            {
                case DslSymbolKind.Node when !string.IsNullOrWhiteSpace(decl.Name):
                    nodeNames.Add(decl.Name);
                    break;
                // 重复 [Edge] 名保留首个（与闸A / 落库的「声明序首个」口径一致）
                case DslSymbolKind.Edge when !string.IsNullOrWhiteSpace(decl.Name):
                    edgesByType.TryAdd(decl.Name, decl);
                    break;
            }
        }

        return new UsharpDeclarationIndex(
            all,
            byKind.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<DslDeclaration>)kv.Value),
            edgesByType,
            nodeNames);
    }

    /// <summary>全部声明（跨文档、声明序）。</summary>
    public IReadOnlyList<DslDeclaration> All => _all;

    /// <summary>某类声明（声明序；无 → 空表）。</summary>
    public IReadOnlyList<DslDeclaration> OfKind(DslSymbolKind kind)
        => _byKind.TryGetValue(kind, out var list) ? list : Array.Empty<DslDeclaration>();

    /// <summary><c>[Node]</c> 声明（声明序；含无名声明，消费方自行过滤）。</summary>
    public IReadOnlyList<DslDeclaration> Nodes => OfKind(DslSymbolKind.Node);

    /// <summary><c>[Edge]</c> 声明（声明序）。</summary>
    public IReadOnlyList<DslDeclaration> Edges => OfKind(DslSymbolKind.Edge);

    /// <summary>已声明 <c>[Node]</c> 名集（非空名）。</summary>
    public IReadOnlySet<string> NodeNames => _nodeNames;

    /// <summary>按名取 <c>[Edge]</c> 声明（重复名保留首个）。</summary>
    public bool TryGetEdgeDecl(string? name, out DslDeclaration decl)
    {
        if (!string.IsNullOrWhiteSpace(name)) return _edgesByType.TryGetValue(name, out decl!);
        decl = null!;
        return false;
    }

    /// <summary>
    /// 边类型的落库 <c>refType</c>：<c>[Edge] ur=</c> 优先，否则用边类型原名（<c>string</c> 通道**不塌缩**）。
    /// </summary>
    public string EdgeRefType(string edgeType)
    {
        ArgumentNullException.ThrowIfNull(edgeType);
        if (!TryGetEdgeDecl(edgeType, out var decl)) return edgeType;

        var ur = decl.Field(UsharpLanguage.FieldUr);
        return string.IsNullOrWhiteSpace(ur) ? edgeType : ur.Trim();
    }
}
