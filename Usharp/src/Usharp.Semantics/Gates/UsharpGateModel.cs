using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 闸A 领域模型：把「已声明集 + 待校验声明 + 共享索引」装配为一次校验的只读输入。
/// 由 <see cref="UsharpValidator.Validate"/> 构造，喂给内核 <c>GateEngine&lt;UsharpGateModel&gt;</c>；
/// 规则只读本模型，不产生副作用（对齐「本层只校验不执行」）。
/// <para>
/// <b>去硬编码（H4 消除）</b>：构造器只收「泛型声明事实 + facet 注册表」；领域视图经
/// <see cref="UsharpFacetViewSet"/> 的**单一实现**读取（D-5：本类不再重复声明视图）。
/// 闸A 语义 = 未装载领域包 ⇒ 空表（<see cref="UsharpFacetViewSet.View{T}"/> 的缺省判据）。
/// </para>
/// </summary>
public sealed class UsharpGateModel : UsharpFacetViewSet
{
    private readonly UsharpDeclarationIdIndex<UsharpFlowDecl> _flowIndex;

    public UsharpGateModel(
        IReadOnlyList<DslDocument> documents,
        UsharpDeclarationIndex declarations,
        DslIndex index,
        UsharpFacetRegistry facets,
        IReadOnlyList<UsharpSourceFile>? sources = null,
        Plate.DslMethod.Parsing.DslPrefixSet? prefixScope = null)
        : base(facets)
    {
        Documents = documents;
        Declarations = declarations;
        Index = index;
        Sources = sources ?? System.Array.Empty<UsharpSourceFile>();
        PrefixScope = prefixScope;

        var flows = Flows;
        // 共享索引：一次构造，规则间复用（避免每条规则重复枚举）。
        _flowIndex = UsharpDeclarationIdIndex<UsharpFlowDecl>.Build(
            flows, flow => flow.FlowId,
            flow => UsharpSourceRef.At(flow.SourceFile, flow.LineNumber));
        FlowIds = new HashSet<string>(_flowIndex.Ids, StringComparer.Ordinal);
        NodeNames = new HashSet<string>(declarations.NodeNames, StringComparer.Ordinal);
        WrittenCoordinates = BuildWrittenCoordinates(Matrices);
        Hierarchy = UsharpHierarchyGraph.Build(Spaces);
    }

    public IReadOnlyList<DslDocument> Documents { get; }

    /// <summary>声明索引（<c>[Node]</c>/<c>[Edge]</c> 扫描的单点来源）。</summary>
    public UsharpDeclarationIndex Declarations { get; }

    public DslIndex Index { get; }

    /// <summary>本次编译的源文本（<c>US0013</c> 前缀可见性判据的唯一读面；空 = 无源文本 ⇒ 该判据跳过）。</summary>
    public IReadOnlyList<UsharpSourceFile> Sources { get; }

    /// <summary>本次编译的前缀作用域（核心 ∪ 已装载包；null = 未提供 ⇒ 该判据跳过）。</summary>
    public Plate.DslMethod.Parsing.DslPrefixSet? PrefixScope { get; }

    /// <summary>观测声明注册中心（可为 null：未启用 <c>[Obs]</c> 闸A 时）。</summary>
    public ObsRegistry? Obs => TryGetFacet<ObsRegistry>(out var obs) ? obs : null;

    // 核心/领域视图（Spaces/Matrices/…/Consts）全部来自 UsharpFacetViewSet（D-5：单一实现）。

    /// <summary>已声明流 id 集（规则 ③ 复用）。</summary>
    public HashSet<string> FlowIds { get; }

    /// <summary>已声明 <c>[Node]</c> 名集（规则 ③ 告警判据复用）。</summary>
    public HashSet<string> NodeNames { get; }

    /// <summary>
    /// 写集：层 Id → 该层矩阵行的输出坐标集（跨块写依赖 `US1017` 的判据输入；写域单一性由 `US1010` 保证）。
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> WrittenCoordinates { get; }

    /// <summary>
    /// 空间划分图（<c>[Space] contains=</c> 遍历**单源**，闸A 与产出面共用同一实现）。
    /// 本编译无划分声明 → null（无层次面）。
    /// </summary>
    public UsharpHierarchyGraph? Hierarchy { get; }

    /// <summary>
    /// 坐标的**非祖先写者层**（写该坐标、且既非本层亦非祖先层的层；按声明序去重）——
    /// `US1017`（写依赖未声明）与 `US1004`（读域不成立）的归因分工依据。
    /// </summary>
    public IReadOnlyList<string> NonAncestorWriters(string spaceId, string key)
    {
        if (string.IsNullOrWhiteSpace(spaceId) || string.IsNullOrWhiteSpace(key))
            return Array.Empty<string>();

        var excluded = new HashSet<string>(Spaces.AncestorSpaces(spaceId), StringComparer.Ordinal) { spaceId };
        var writers = new List<string>();
        foreach (var (layer, writes) in WrittenCoordinates)
            if (!excluded.Contains(layer) && writes.Contains(key)) writers.Add(layer);
        return writers;
    }

    /// <summary>
    /// 覆盖某层的流：<c>over=</c> 该层自身 ∨ 该层祖先层（层维度判据的**唯一实现**；
    /// 闸A 各规则不得自行复写「哪条流覆盖该层」——对齐 <c>Strategy.SingleImplementation</c>）。
    /// </summary>
    public IEnumerable<UsharpFlowDecl> CoveringFlows(string layerId)
    {
        if (string.IsNullOrWhiteSpace(layerId)) yield break;

        var covering = new HashSet<string>(Spaces.AncestorSpaces(layerId), StringComparer.Ordinal) { layerId };
        foreach (var flow in Flows)
            if (flow.OverSpaceId is { } over && covering.Contains(over)) yield return flow;
    }

    /// <summary>按流号取流声明（未声明 → null；闸A 端点解析单源；重复 ⇒ FirstWins）。</summary>
    public UsharpFlowDecl? FlowById(string flowId)
        => _flowIndex.TryGet(flowId, out var flow) ? flow : null;

    private static IReadOnlyDictionary<string, HashSet<string>> BuildWrittenCoordinates(
        IReadOnlyList<UsharpMatrixDecl> matrices)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var matrix in matrices)
        {
            if (string.IsNullOrWhiteSpace(matrix.OverSpaceId)) continue;
            if (!map.TryGetValue(matrix.OverSpaceId, out var keys))
                map[matrix.OverSpaceId] = keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in matrix.Rows) keys.Add(row.OutputKey);
        }
        return map;
    }
}
