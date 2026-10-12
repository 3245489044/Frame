using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ⑩：判据读法闸A（<c>[Matrix] over=&lt;kind=terms 空间&gt;</c>）—— <c>US1101</c>–<c>US1106</c>（E1/U2b）。
/// <para>
/// 覆盖三处必判（T-6 §3 解禁先决 ③）：词元越界（<c>US1102</c>）/ 优先级冲突（<c>US1103</c>）/ 吸收集未声明（<c>US1104</c>）；
/// 并守读法边界（<c>US1105</c>）与复合特征可解析（<c>US1106</c>）。
/// </para>
/// <para>只校验声明面；求值语义归既有 <c>BasicMethod.Declarative.Graph.DecisionGraph</c>（不新增裁决机制）。</para>
/// </summary>
internal sealed class DecisionGateRule : IGateRule<UsharpGateModel>
{
    /// <summary>合法象限（T-6 §1 的 I/E/O 三象限；单字母，唯一真源）。</summary>
    private static readonly IReadOnlyList<string> Zones = new[] { "I", "E", "O" };

    public string Name => "usharp.decision";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        // ① 词元空间声明面：zone= 缺失/非法、词元集为空（US1101）
        foreach (var space in model.Spaces.Spaces)
        {
            if (space.Kind != UsharpSpaceKind.Terms) continue;

            foreach (var axis in space.Axes)
            {
                if (string.IsNullOrWhiteSpace(axis.Zone) || !Zones.Contains(axis.Zone, StringComparer.Ordinal))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TermZoneInvalid,
                        $"[Space] {space.SpaceId}: 词元轴 '{axis.Name}' 的 zone= 缺失或非法（合法：{string.Join("/", Zones)}）",
                        space.SourceFile, space.LineNumber));

                if (axis.Terms.Count == 0)
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TermZoneInvalid,
                        $"[Space] {space.SpaceId}: 词元轴 '{axis.Name}' 未声明词元（terms=<t1>,<t2>）",
                        space.SourceFile, space.LineNumber));
            }
        }

        // ② 矩阵：读法边界 + 判据体校验
        foreach (var matrix in model.Matrices)
        {
            if (string.IsNullOrWhiteSpace(matrix.OverSpaceId)) continue;
            if (!model.Spaces.TryGet(matrix.OverSpaceId, out var space)) continue;   // US1001 已报

            var decision = UsharpDecisionDecl.From(matrix);

            if (space.Kind != UsharpSpaceKind.Terms)
            {
                // 读法混用：非词元空间出现判据续行 ⇒ 显式报错（不得按线性表达式静默吞下）。
                if (decision.HasDecisionSurface || decision.UnparsedLines.Count > 0)
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionShapeInvalid,
                        $"[Matrix] {matrix.MatrixId}: 空间 '{space.SpaceId}' 是 {KindName(space.Kind)} 型，"
                        + "不得含判据续行（layer/rule/cross）—— 判据读法须 over=<kind=terms 空间>",
                        matrix.SourceFile, matrix.LineNumber));
                continue;
            }

            ValidateDecision(matrix, decision, space, sink);
        }
    }

    private static void ValidateDecision(
        UsharpMatrixDecl matrix,
        UsharpDecisionDecl decision,
        UsharpSpaceDecl space,
        IConversionDiagnosticSink sink)
    {
        // 形态：无法归位的判据行（层号不可解析 / 层行缺失 / 缺 output= / 缺 label=/priority=）
        foreach (var line in decision.UnparsedLines)
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionShapeInvalid,
                $"[Matrix] {matrix.MatrixId}: 判据续行无法归位（'{line}'）—— 形如 "
                + "layer <层> | output=<输出轴> 或 rule <层> | label=<标签> | priority=<n> | when=<轴>.<词元>",
                matrix.SourceFile, matrix.LineNumber));

        if (decision.Layers.Count == 0)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionShapeInvalid,
                $"[Matrix] {matrix.MatrixId}: 判据读法未声明任何层（layer <层> | output=<输出轴>）",
                matrix.SourceFile, matrix.LineNumber));
            return;
        }

        // 层间回引白名单：输出轴 → 该层标签集（规则可引用上游层输出轴上的标签，DecisionGraph 的层间回引语义）。
        var layerAxes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var l in decision.Layers)
            if (!layerAxes.ContainsKey(l.OutputAxis))
                layerAxes[l.OutputAxis] = new HashSet<string>(l.Rules.Select(r => r.Label), StringComparer.Ordinal);

        foreach (var group in decision.Layers.GroupBy(l => l.LayerId, StringComparer.Ordinal))
        {
            var layer = group.First();

            if (group.Count() > 1)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionShapeInvalid,
                    $"[Matrix] {matrix.MatrixId}: 层 '{layer.LayerId}' 重复声明（{group.Count()} 次；层名须唯一）",
                    matrix.SourceFile, matrix.LineNumber));

            // US1103：面内 priority 须全序显式支配
            foreach (var duplicate in layer.Rules.GroupBy(r => r.Priority).Where(g => g.Count() > 1))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionPriorityConflict,
                    $"[Matrix] {matrix.MatrixId}: 层 '{layer.LayerId}' 内 priority={duplicate.Key} 被 "
                    + $"{string.Join("/", duplicate.Select(r => r.Label))} 重复声明（面内须全序）",
                    matrix.SourceFile, matrix.LineNumber));

            // US1104：吸收标签须在本层标签集内
            var labels = new HashSet<string>(layer.Rules.Select(r => r.Label), StringComparer.Ordinal);
            foreach (var absorbed in layer.Absorb)
                if (!labels.Contains(absorbed))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionAbsorbUndeclared,
                        $"[Matrix] {matrix.MatrixId}: 层 '{layer.LayerId}' 的 absorb='{absorbed}' "
                        + $"不在本层标签集（{string.Join("/", labels)}）内",
                        matrix.SourceFile, matrix.LineNumber));

            foreach (var rule in layer.Rules)
            {
                // US1106：复合特征须已声明
                if (rule.CrossName is not null && decision.Cross(rule.CrossName) is null)
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionCrossUndeclared,
                        $"[Matrix] {matrix.MatrixId}: 规则 '{rule.Label}' 引用未声明的复合特征 'cross.{rule.CrossName}'",
                        matrix.SourceFile, matrix.LineNumber));

                // US1102：词元引用须可解析（<轴>.<词元> = 词元空间输入轴 ∪ 层输出轴回引）
                foreach (var reference in rule.When) ReportTerm(matrix, space, rule, reference, layerAxes, sink);
            }
        }

        // US1106：复合特征自身须非空（空 when 已在解析期归 unparsed）
        foreach (var cross in decision.Crosses)
            if (cross.When.Count == 0)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionCrossUndeclared,
                    $"[Matrix] {matrix.MatrixId}: 复合特征 'cross.{cross.Name}' 的 when= 为空",
                    matrix.SourceFile, matrix.LineNumber));
    }

    private static void ReportTerm(
        UsharpMatrixDecl matrix,
        UsharpSpaceDecl space,
        UsharpDecisionRule rule,
        string reference,
        IReadOnlyDictionary<string, HashSet<string>> layerAxes,
        IConversionDiagnosticSink sink)
    {
        var separator = reference.IndexOf(UsharpLanguage.TermSeparator);
        if (separator <= 0 || separator == reference.Length - 1)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionTermUndeclared,
                $"[Matrix] {matrix.MatrixId}: 规则 '{rule.Label}' 的词元引用 '{reference}' 形状非法（须形如 <轴>.<词元>）",
                matrix.SourceFile, matrix.LineNumber));
            return;
        }

        var axisName = reference[..separator];
        var term = reference[(separator + 1)..];

        // ① 层间回引：引用某层输出轴上的标签（DecisionGraph 允许下游层消费上游层产出词元）。
        if (layerAxes.TryGetValue(axisName, out var produced))
        {
            if (produced.Contains(term)) return;
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionTermUndeclared,
                $"[Matrix] {matrix.MatrixId}: 规则 '{rule.Label}' 引用层输出轴 '{axisName}' 上未产出的标签 '{term}'"
                + $"（合法标签：{string.Join("/", produced)}）",
                matrix.SourceFile, matrix.LineNumber));
            return;
        }

        var axis = space.Axes.FirstOrDefault(a => string.Equals(a.Name, axisName, StringComparison.Ordinal));

        if (axis is null)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionTermUndeclared,
                $"[Matrix] {matrix.MatrixId}: 规则 '{rule.Label}' 引用未声明轴 '{axisName}'"
                + $"（输入轴：{string.Join("/", space.Axes.Select(a => a.Name))}；层输出轴：{string.Join("/", layerAxes.Keys)}）",
                matrix.SourceFile, matrix.LineNumber));
            return;
        }

        if (!axis.Terms.Contains(term, StringComparer.Ordinal))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DecisionTermUndeclared,
                $"[Matrix] {matrix.MatrixId}: 规则 '{rule.Label}' 引用轴 '{axisName}' 上未声明的词元 '{term}'"
                + $"（合法词元：{string.Join("/", axis.Terms)}）",
                matrix.SourceFile, matrix.LineNumber));
    }

    private static string KindName(UsharpSpaceKind kind) => kind switch
    {
        UsharpSpaceKind.Keys => UsharpLanguage.KindKeys,
        UsharpSpaceKind.Values => UsharpLanguage.KindValues,
        _ => UsharpLanguage.KindTerms,
    };
}
