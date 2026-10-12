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
/// <see cref="Facet{T}"/> 读取，属性保留为**薄访问器**（签名与语义逐字不变）。
/// </para>
/// </summary>
public sealed class UsharpGateModel
{
    private readonly UsharpFacetRegistry _facets;
    private readonly Dictionary<string, UsharpFlowDecl> _flowsById = new(StringComparer.Ordinal);

    public UsharpGateModel(
        IReadOnlyList<DslDocument> documents,
        UsharpDeclarationIndex declarations,
        DslIndex index,
        UsharpFacetRegistry facets)
    {
        Documents = documents;
        Declarations = declarations;
        Index = index;
        _facets = facets;

        var flows = Flows;
        // 共享索引：一次构造，规则间复用（避免每条规则重复枚举）。
        FlowIds = new HashSet<string>(flows.Select(f => f.FlowId), StringComparer.Ordinal);
        NodeNames = new HashSet<string>(declarations.NodeNames, StringComparer.Ordinal);
        WrittenCoordinates = BuildWrittenCoordinates(Matrices);
        Hierarchy = UsharpHierarchyGraph.Build(Spaces);
        foreach (var flow in flows)
            if (!string.IsNullOrWhiteSpace(flow.FlowId)) _flowsById.TryAdd(flow.FlowId, flow);
    }

    public IReadOnlyList<DslDocument> Documents { get; }

    /// <summary>声明索引（<c>[Node]</c>/<c>[Edge]</c> 扫描的单点来源）。</summary>
    public UsharpDeclarationIndex Declarations { get; }

    public DslIndex Index { get; }

    /// <summary>取 facet 视图（未注册 → 抛）。</summary>
    public T Facet<T>() where T : class => _facets.Facet<T>();

    /// <summary>取 facet 视图（未注册 → false）。</summary>
    public bool TryGetFacet<T>(out T facet) where T : class => _facets.TryGetFacet(out facet);

    public SpaceRegistry Spaces => _facets.Facet<SpaceRegistry>();

    public IReadOnlyList<UsharpMatrixDecl> Matrices => _facets.Facet<IReadOnlyList<UsharpMatrixDecl>>();

    public IReadOnlyList<UsharpFlowDecl> Flows => _facets.Facet<IReadOnlyList<UsharpFlowDecl>>();

    public IReadOnlyList<UsharpRefDecl> Refs => _facets.Facet<IReadOnlyList<UsharpRefDecl>>();

    /// <summary>观测声明注册中心（可为 null：未启用 <c>[Obs]</c> 闸A 时）。</summary>
    public ObsRegistry? Obs => _facets.TryGetFacet<ObsRegistry>(out var obs) ? obs : null;

    /// <summary><c>[Domain]</c> 域定义声明（U1；空表 = 本编译无域定义面）。</summary>
    public IReadOnlyList<UsharpDomainDecl> Domains
        => _facets.TryGetFacet<IReadOnlyList<UsharpDomainDecl>>(out var d) ? d : Array.Empty<UsharpDomainDecl>();

    /// <summary><c>[Gather]</c> 聚拢声明（U2；空表 = 本编译无收集端口声明）。</summary>
    public IReadOnlyList<UsharpGatherDecl> Gathers
        => _facets.TryGetFacet<IReadOnlyList<UsharpGatherDecl>>(out var g) ? g : Array.Empty<UsharpGatherDecl>();

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

    /// <summary>按流号取流声明（未声明 → null；闸A 端点解析单源）。</summary>
    public UsharpFlowDecl? FlowById(string flowId)
        => _flowsById.TryGetValue(flowId ?? "", out var flow) ? flow : null;

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
