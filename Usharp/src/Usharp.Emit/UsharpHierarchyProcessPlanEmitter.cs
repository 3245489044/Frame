using Modules.DeclarativeDataflow.Processing;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次处理计划（<c>[Flow] order=/stop=/budget=</c>）的发射产物：计划 + 本流行的计划相关诊断。
/// <para>
/// 诊断口径：判据唯一权威 = 闸A 的 <c>ProcessPlanGateRule</c>（<c>US1012</c>/<c>US1018</c>）；本层只**投影**
/// 同一编译中挂在当前流声明行上的计划诊断，不产生第二份判据（回退口径经 <see cref="UsharpPlanFields"/> 共用）。
/// </para>
/// </summary>
public sealed record UsharpHierarchyPlanResult(
    HierarchyProcessPlan? Plan,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    /// <summary>发射是否失败（含闸A 投影出的 error；warn 不算失败）。</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no plan diagnostics)");
}

/// <summary>
/// <c>[Flow]</c> → <see cref="HierarchyProcessPlan"/> 发射器（处理编排：方向 / 展开止层 / 预算）。
/// <para>
/// 只装配计划数据：遍历、自环截断、三重护栏与子结论聚合仍由内核 <c>HierarchyProcessEngine</c> 承担，本层不产引擎。
/// </para>
/// </summary>
public static class UsharpHierarchyProcessPlanEmitter
{
    /// <summary>本层透出的计划相关闸A 码（判据来源 = <c>ProcessPlanGateRule</c>；名单单源 = <see cref="UsharpCodes.PlanRelated"/>）。</summary>
    private static readonly string[] PlanCodes = UsharpCodes.PlanRelated.ToArray();

    /// <summary>发射指定流的处理计划（未声明 / 计划字段非法时按缺省回退，并把闸A 诊断原样透出）。</summary>
    public static UsharpHierarchyPlanResult Emit(UsharpCompilation compilation, string flowId)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var flow = compilation.Flows.FirstOrDefault(f => string.Equals(f.FlowId, flowId, StringComparison.Ordinal));
        if (flow is null)
            return new UsharpHierarchyPlanResult(null, new[]
            {
                UsharpDiagnostic.Error(UsharpCodes.FlowNotDeclared,
                    $"[Flow] '{flowId}' 未声明（本编译可编排的流：{string.Join(", ", compilation.Flows.Select(f => f.FlowId))}）",
                    compilation.Documents.FirstOrDefault()?.FilePath ?? "(request)", 0),
            });

        // 回退口径与闸A 同源（未知 order → BottomUp；非法 budget → 无显式预算）。
        UsharpPlanFields.TryOrder(flow, out var order);
        UsharpPlanFields.TryBudget(flow, out var budget);

        var plan = new HierarchyProcessPlan(
            order,
            budget,
            ProcessIdOverride: null,
            StopLevelId: flow.StopSpaceId);

        var diagnostics = compilation.Diagnostics
            .Where(d => string.Equals(d.SourceFile, flow.SourceFile, StringComparison.Ordinal)
                        && d.LineNumber == flow.LineNumber
                        && PlanCodes.Contains(d.Code, StringComparer.Ordinal))
            .ToList();

        return new UsharpHierarchyPlanResult(plan, diagnostics);
    }
}
