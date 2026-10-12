using Modules.DeclarativeDataflow.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ⑤：空间划分（<c>[Space] contains=</c>）闸A —— <c>US1008</c>（层图非法）/ <c>US1009</c>（坐标子集不成立）。
/// <para>
/// **职责二分**（对齐设计方案 §3.6）：① <b>语言面判据归本规则</b>（<c>contains=</c> 形状 / 父层是否已声明 /
/// 子空间坐标是否为父空间坐标子集 —— 框架不解释坐标，无从代判）；② <b>层图结构判据委托内核</b>
/// <see cref="HierarchySchemaValidator"/>（自根可达 / 非自环层间环），**不自行实现**可达性与环检测
/// （<c>Strategy.SingleImplementation</c>：同一图遍历不得两处实现）。
/// </para>
/// <para>
/// **森林投影**：Usharp 的空间划分天然是**森林**（可多个顶层空间），而内核 <see cref="HierarchySchema"/>
/// 是单根模型。故校验前注入一个**合成根**（仅存在于校验投影，不进入任何产出）：合成根包含全部顶层划分空间，
/// 于是"落在环里的空间"自然表现为合成根不可达 + 层间环，两类问题都由内核判出。
/// 自环（<c>contains=自我:edge</c> = 递归划分）按内核口径**合法**，故不计入"有父"（不推进可达性）。
/// </para>
/// </summary>
internal sealed class HierarchyGateRule : IGateRule<UsharpGateModel>
{
    /// <summary>合成根层 Id（校验投影专用；不含用户可见语义）。</summary>
    private const string VirtualRootLevelId = "(usharp.forest)";

    /// <summary>合成根的包含边类型（内核要求包含边类型非空）。</summary>
    private const string VirtualRootEdgeType = "usharp_forest_root";

    public string Name => "usharp.hierarchy";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var declared = model.Spaces.Spaces.Where(space => space.Contains.Count > 0).ToList();

        // ① 语言面判据：形状 / 父层已声明 / 坐标子集。同时收拢可进层图的包含对。
        var pairs = new List<(string Parent, string Child, string Edge)>();
        foreach (var space in declared)
            foreach (var containment in space.Contains)
            {
                if (containment.ParentSpaceId.Length == 0 || containment.EdgeType.Length == 0)
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SpaceHierarchyInvalid,
                        $"[Space] {space.SpaceId}: contains 形状非法 '{Describe(containment)}'"
                        + "（须为 <父空间>:<包含边类型>，多父层以 ';' 分段）",
                        space.SourceFile, space.LineNumber));
                    continue;
                }

                if (!model.Spaces.TryGet(containment.ParentSpaceId, out var parent))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SpaceHierarchyInvalid,
                        $"[Space] {space.SpaceId}: contains 指向未声明的父空间 '{containment.ParentSpaceId}'（层图悬空）",
                        space.SourceFile, space.LineNumber));
                    continue;
                }

                foreach (var axis in space.Axes)
                    if (!model.Spaces.HasCoordinate(parent.SpaceId, axis.Name, out _))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SubSpaceAxisUndeclared,
                            $"[Space] {space.SpaceId}: 坐标 '{axis.Name}' 未在父空间 '{parent.SpaceId}' 声明"
                            + "（子集不成立 ⇒ 划分非法）",
                            space.SourceFile, space.LineNumber));

                pairs.Add((parent.SpaceId, space.SpaceId, containment.EdgeType));
            }

        // ② 层图结构判据：无划分声明则无事可查（不调用内核、不产生信息型噪声）。
        if (pairs.Count == 0) return;

        foreach (var issue in HierarchySchemaValidator.Validate(BuildForestSchema(pairs)))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SpaceHierarchyInvalid,
                $"空间划分层图非法：{issue}", declared[0].SourceFile, declared[0].LineNumber));
    }

    /// <summary>
    /// 把「包含对」投影为单根 <see cref="HierarchySchema"/>：合成根 → 全部顶层空间（含自环者的自环不计入"有父"）。
    /// 层 Id 用空间 Id 原文（校验只关心层图结构，不解释实体语义）。
    /// </summary>
    private static HierarchySchema BuildForestSchema(IReadOnlyList<(string Parent, string Child, string Edge)> pairs)
    {
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        var levelIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { VirtualRootLevelId };

        foreach (var (parent, child, _) in pairs)
        {
            if (seen.Add(parent)) levelIds.Add(parent);
            if (seen.Add(child)) levelIds.Add(child);
            if (!string.Equals(parent, child, StringComparison.Ordinal)) childIds.Add(child);
        }

        var contains = new Dictionary<string, List<HierarchyContainment>>(StringComparer.Ordinal);
        foreach (var (parent, child, edge) in pairs)
        {
            if (!contains.TryGetValue(parent, out var list))
                contains[parent] = list = new List<HierarchyContainment>();
            list.Add(new HierarchyContainment(child, edge));
        }

        var levels = new List<HierarchyLevelSpec>(levelIds.Count + 1)
        {
            new(VirtualRootLevelId, VirtualRootLevelId,
                levelIds.Where(id => !childIds.Contains(id))
                        .Select(id => new HierarchyContainment(id, VirtualRootEdgeType))
                        .ToList()),
        };

        foreach (var id in levelIds)
            levels.Add(new HierarchyLevelSpec(id, id,
                contains.TryGetValue(id, out var list) ? list : new List<HierarchyContainment>()));

        return new HierarchySchema(VirtualRootLevelId, levels);
    }

    private static string Describe(UsharpContainmentDecl containment)
        => containment.EdgeType.Length == 0 ? containment.ParentSpaceId : $"{containment.ParentSpaceId}:{containment.EdgeType}";
}