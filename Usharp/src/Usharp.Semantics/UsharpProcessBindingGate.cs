using Modules.DeclarativeDataflow.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ⑥：层处理器绑定单源（<c>process=</c>）—— <c>US1011</c>。
/// <para>
/// 「小矩阵 = 该层的处理代数」：一个层**恰有一个**处理代数声明者（<c>[Matrix]</c> 或 <c>[Flow]</c>）。
/// 与内核 <c>HierarchyLevelSpec.ProcessId</c> 单字段模型同构（一层一个 ProcessId ⇒ 多声明者在框架侧必静默覆盖）。
/// 空值（<c>process=</c> 在场但无值）亦属违例 —— 不做"空即未绑定"的静默降级。
/// </para>
/// </summary>
internal sealed class ProcessBindingGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.process-binding";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var bindings = new Dictionary<string, (string Owner, string File, int Line)>(StringComparer.Ordinal);

        void Bind(string? spaceId, string? processId, string owner, string file, int line)
        {
            if (processId is null) return;   // 未声明 process= ⇒ 未绑定（合法）

            if (string.IsNullOrWhiteSpace(spaceId))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProcessorBindingNotSingle,
                    $"{owner}: 声明了 process= 但缺 over=（无绑定目标层）", file, line));
                return;
            }

            if (processId.Length == 0)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProcessorBindingNotSingle,
                    $"{owner}: process= 为空（层 '{spaceId}' 的处理代数须具名）", file, line));
                return;
            }

            if (bindings.ContainsKey(spaceId))
                return;   // 重复声明者由 MultiBoundLayers 统一裁决（一层一报，不逐条重复）

            bindings[spaceId] = (owner, file, line);
        }

        foreach (var m in model.Matrices)
            Bind(m.OverSpaceId, m.ProcessId, $"[Matrix] {m.MatrixId}", m.SourceFile, m.LineNumber);

        foreach (var f in model.Flows)
            Bind(f.OverSpaceId, f.ProcessId, $"[Flow] {f.FlowId}", f.SourceFile, f.LineNumber);

        // 一层多绑定：一次一因（判定单源 = UsharpProcessBindings；产出面据此「不产出」）
        foreach (var layer in UsharpProcessBindings.MultiBoundLayers(
                     model.Matrices.Where(m => m.ProcessId is not null).Select(m => (m.OverSpaceId, m.ProcessId))
                         .Concat(model.Flows.Where(f => f.ProcessId is not null).Select(f => (f.OverSpaceId, f.ProcessId)))))
        {
            var owners = model.Matrices.Where(m => string.Equals(m.OverSpaceId, layer, StringComparison.Ordinal) && m.ProcessId is not null)
                    .Select(m => $"[Matrix] {m.MatrixId}")
                .Concat(model.Flows.Where(f => string.Equals(f.OverSpaceId, layer, StringComparison.Ordinal) && f.ProcessId is not null)
                    .Select(f => $"[Flow] {f.FlowId}"));

            var first = model.Matrices.FirstOrDefault(m => string.Equals(m.OverSpaceId, layer, StringComparison.Ordinal) && m.ProcessId is not null);
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProcessorBindingNotSingle,
                $"层 '{layer}' 的处理代数重复声明：{string.Join(" / ", owners)} —— 一层恰一个处理器声明者",
                first?.SourceFile ?? model.Flows.First(f => string.Equals(f.OverSpaceId, layer, StringComparison.Ordinal)).SourceFile,
                first?.LineNumber ?? 0));
        }
    }
}

/// <summary>
/// 规则 ⑦：处理编排字段闸A（<c>order=</c> / <c>budget=</c>）—— <c>US1012</c>（warn）/ <c>US1018</c>（warn）。
/// <para>
/// <c>US1012</c>：含自环（<c>contains=自我:edge</c> ⇒ 不定深度）的层所属流未声明 <c>budget=</c> ——
/// 预算合法性由框架运行期 <c>ProcessBudget</c> 强制，装载期只把「不定深度风险」前移可见化。
/// </para>
/// <para>
/// <c>US1018</c>：<c>order=</c> 未知值 / <c>budget=</c> 非两段正整数 —— 按缺省处理但**不静默**
/// （取值合法性与回退口径的唯一实现 = <see cref="UsharpPlanFields"/>，与计划发射层共用）。
/// </para>
/// </summary>
internal sealed class ProcessPlanGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.process-plan";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        // US1012（按**层维度**判定）：含自环的层须被「覆盖它的流」（over= 该层自身 ∨ 该层祖先层）以 budget= 覆盖；
        // 无任何流覆盖的自环层不报（无声明面可承载预算 —— US1013 待开工面，本规则不猜）。
        foreach (var layer in SelfRecursiveLayers(model))
        {
            var covering = CoveringFlows(model, layer.SpaceId).ToList();
            if (covering.Count == 0) continue;
            if (covering.Any(f => UsharpPlanFields.TryBudget(f, out var budget) && budget is not null)) continue;

            var owner = covering[0];
            sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.SelfRecursiveBudgetMissing,
                $"[Flow] {owner.FlowId}: 层 '{layer.SpaceId}' 含自环（递归划分 = 不定深度）但覆盖它的流均未声明 "
                + "budget=<maxNodes>;<maxDepth>（预算风险前移；运行期由 ProcessBudget 强制收口）",
                owner.SourceFile, owner.LineNumber));
        }

        foreach (var flow in model.Flows)
        {
            if (!UsharpPlanFields.TryOrder(flow, out _))
                sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.PlanFieldInvalid,
                    $"[Flow] {flow.FlowId}: 未知处理方向 order='{flow.Order}'"
                    + $"（合法值：{string.Join("/", UsharpPlanFields.Orders)}），按 {UsharpLanguage.OrderBottomUp} 处理",
                    flow.SourceFile, flow.LineNumber));

            if (!UsharpPlanFields.TryBudget(flow, out _))
                sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.PlanFieldInvalid,
                    $"[Flow] {flow.FlowId}: 预算 budget='{flow.BudgetRaw}' 非法"
                    + "（须为 <maxNodes>;<maxDepth> 两段正整数），按未声明处理",
                    flow.SourceFile, flow.LineNumber));
        }
    }

    /// <summary>含自环声明（<c>contains=自我:edge</c>）的层集（声明序）。</summary>
    private static IEnumerable<UsharpSpaceDecl> SelfRecursiveLayers(UsharpGateModel model)
        => model.Spaces.Spaces.Where(s => s.Contains.Any(c => string.Equals(c.ParentSpaceId, s.SpaceId, StringComparison.Ordinal)));

    /// <summary>覆盖某层的流：<c>over=</c> 该层自身 ∨ 该层祖先层（层维度判据的直接来源）。</summary>
    private static IEnumerable<UsharpFlowDecl> CoveringFlows(UsharpGateModel model, string layerId)
    {
        var covering = new HashSet<string>(model.Spaces.AncestorSpaces(layerId), StringComparer.Ordinal) { layerId };
        return model.Flows.Where(f => f.OverSpaceId is { } over && covering.Contains(over));
    }
}
