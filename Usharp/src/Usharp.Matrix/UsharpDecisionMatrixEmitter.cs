using BasicMethod.Declarative.Graph;
using BasicMethod.Declarative.Spec;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// 判据矩阵发射器（E1/U2b）：<c>[Matrix] over=&lt;kind=terms 空间&gt;</c> 的 <c>layer</c>/<c>rule</c>/<c>cross</c> 续行
/// → 既有 <see cref="DecisionGraph"/>（面内优先级 + 命中即停 + 吸收短路）。
/// <para>
/// 闸A 唯一权威 = <see cref="UsharpValidator"/>（<c>US1101</c>–<c>US1106</c> 的判据与归因）；
/// 本类只做装配：不通过闸A 的行不入绑定、不重复映射闸A 码（同一缺陷只报一次）；
/// 装配期异常（层名/输出轴重复）转 <see cref="UsharpCodes.DecisionShapeInvalid"/>，不静默降级。
/// </para>
/// </summary>
public static class UsharpDecisionMatrixEmitter
{
    /// <summary>装配一个判据矩阵（<paramref name="space"/> 须为 <c>over=</c> 指向的词元空间声明）。</summary>
    /// <param name="decl">矩阵声明（<see cref="UsharpMatrixDecl.RawLines"/> 为续行真源）。</param>
    /// <param name="space">词元空间声明。</param>
    public static UsharpMatrixBinding Emit(UsharpMatrixDecl decl, UsharpSpaceDecl space)
    {
        ArgumentNullException.ThrowIfNull(decl);
        ArgumentNullException.ThrowIfNull(space);

        var diagnostics = new List<UsharpDiagnostic>();
        var decision = UsharpDecisionDecl.From(decl);

        // 复合特征：同名即共享（一处定义、多面引用同一实例）。
        var crosses = new Dictionary<string, CrossFeature>(StringComparer.Ordinal);
        foreach (var cross in decision.Crosses)
        {
            if (cross.When.Count == 0) continue;   // 闸A（US1106）已报
            crosses[cross.Name] = new CrossFeature(cross.Name, cross.When.Select(Term).ToList());
        }

        var layers = new List<DecisionLayer>(decision.Layers.Count);
        foreach (var layer in decision.Layers)
        {
            var decisionLayer = new DecisionLayer(layer.LayerId, layer.OutputAxis, layer.Absorb);
            foreach (var rule in layer.Rules)
            {
                if (rule.CrossName is not null && crosses.TryGetValue(rule.CrossName, out var cross))
                {
                    decisionLayer.Add(rule.Label, rule.Priority, cross);
                    continue;
                }

                var required = rule.When.Select(Term).ToList();
                if (required.Count == 0) continue;   // 闸A（US1102）已报
                decisionLayer.Add(new LayerRule(rule.Label, rule.Priority, required));
            }
            layers.Add(decisionLayer);
        }

        DecisionGraph? graph = null;
        if (layers.Count > 0)
        {
            try
            {
                graph = new DecisionGraph(layers);
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.DecisionShapeInvalid,
                    $"[Matrix] {decl.MatrixId}: 判据图装配失败（{ex.Message}）",
                    decl.SourceFile, decl.LineNumber));
            }
        }

        return new UsharpMatrixBinding(decl.MatrixId, UsharpMatrixKind.Decision, space.SpaceId,
            Array.Empty<UsharpMatrixRowBinding>(), null, null, null, diagnostics, decl.SourceFile, decl.LineNumber)
        {
            DecisionGraph = graph,
        };
    }

    /// <summary>
    /// <c>&lt;轴&gt;.&lt;词元&gt;</c> → 内核词元（形状非法由闸A <c>US1102</c> 报；此处只装配，不二次判据）。
    /// </summary>
    private static SemanticTerm Term(string reference)
    {
        var separator = reference.IndexOf(UsharpLanguage.TermSeparator);
        return separator <= 0
            ? new SemanticTerm(reference, reference)
            : new SemanticTerm(reference[..separator], reference[(separator + 1)..]);
    }
}
