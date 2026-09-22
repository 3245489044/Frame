using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 闸A 领域模型：把「已声明集 + 待校验声明 + 共享索引」装配为一次校验的只读输入。
/// 由 <see cref="UsharpValidator.Validate"/> 构造，喂给内核 <c>GateEngine&lt;UsharpGateModel&gt;</c>；
/// 规则只读本模型，不产生副作用（对齐「本层只校验不执行」）。
/// </summary>
public sealed class UsharpGateModel
{
    public UsharpGateModel(
        IReadOnlyList<DslDocument> documents,
        UsharpDeclarationIndex declarations,
        DslIndex index,
        SpaceRegistry spaces,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs,
        ObsRegistry? obs)
    {
        Documents = documents;
        Declarations = declarations;
        Index = index;
        Spaces = spaces;
        Matrices = matrices;
        Flows = flows;
        Refs = refs;
        Obs = obs;

        // 共享索引：一次构造，规则间复用（避免每条规则重复枚举）。
        FlowIds = new HashSet<string>(flows.Select(f => f.FlowId), StringComparer.Ordinal);
        NodeNames = new HashSet<string>(declarations.NodeNames, StringComparer.Ordinal);
        WrittenCoordinates = BuildWrittenCoordinates(matrices);
        UpstreamConnectedLayers = BuildUpstreamConnectedLayers(flows);
    }

    public IReadOnlyList<DslDocument> Documents { get; }

    /// <summary>声明索引（<c>[Node]</c>/<c>[Edge]</c> 扫描的单点来源）。</summary>
    public UsharpDeclarationIndex Declarations { get; }

    public DslIndex Index { get; }

    public SpaceRegistry Spaces { get; }

    public IReadOnlyList<UsharpMatrixDecl> Matrices { get; }

    public IReadOnlyList<UsharpFlowDecl> Flows { get; }

    public IReadOnlyList<UsharpRefDecl> Refs { get; }

    /// <summary>观测声明注册中心（可为 null：未启用 <c>[Obs]</c> 闸A 时）。</summary>
    public ObsRegistry? Obs { get; }

    /// <summary>已声明流 id 集（规则 ③ 复用）。</summary>
    public HashSet<string> FlowIds { get; }

    /// <summary>已声明 <c>[Node]</c> 名集（规则 ③ 告警判据复用）。</summary>
    public HashSet<string> NodeNames { get; }

    /// <summary>
    /// 写集：层 Id → 该层矩阵行的输出坐标集（跨块写依赖 `US1017` 的判据输入；写域单一性由 `US1010` 保证）。
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> WrittenCoordinates { get; }

    /// <summary>
    /// 显式连接的上游层：层 Id → 该层各流经 <c>connects=</c> 指向的上游流所在层
    /// （`US1017` 的「依赖是否已声明」判据来源；读域仍 = 自身 ∪ 祖先闭包，见设计方案 §10.4 实施修正 ④）。
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> UpstreamConnectedLayers { get; }

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

    private static IReadOnlyDictionary<string, HashSet<string>> BuildUpstreamConnectedLayers(IReadOnlyList<UsharpFlowDecl> flows)
    {
        var byId = new Dictionary<string, UsharpFlowDecl>(StringComparer.Ordinal);
        foreach (var flow in flows)
            if (!string.IsNullOrWhiteSpace(flow.FlowId)) byId.TryAdd(flow.FlowId, flow);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            if (string.IsNullOrWhiteSpace(flow.OverSpaceId)) continue;
            foreach (var connects in flow.Connects)
            {
                // 端点不可解析（未声明 / 自连）由闸A（US1015）报出，此处不猜。
                if (!byId.TryGetValue(connects.UpstreamFlowId, out var upstream)) continue;
                if (string.IsNullOrWhiteSpace(upstream.OverSpaceId)) continue;
                if (!map.TryGetValue(flow.OverSpaceId, out var layers))
                    map[flow.OverSpaceId] = layers = new HashSet<string>(StringComparer.Ordinal);
                layers.Add(upstream.OverSpaceId);
            }
        }
        return map;
    }
}
