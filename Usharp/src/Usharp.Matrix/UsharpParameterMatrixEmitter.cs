using BasicMethod.Declarative.Spec;
using Strategy.DecisionRules;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// 值域矩阵发射器：<c>[Space] kind=values</c> → <see cref="ParameterSchema"/>；
/// <c>[Matrix] over=&lt;值型空间&gt;</c> → <see cref="ParameterMatrix"/>（整向量一次变换 <c>x′ = Quantize(W·x+b)</c>）。
/// <para>闸A 唯一权威 = <see cref="UsharpValidator"/>（US1003/US1010/US1004 的判据与归因）；
/// 本类只做装配：装载期闸A 不通过的行不入绑定、不重复映射诊断码（同一缺陷只报一次）。
/// 行右式不可解析（US0004）属本层判据（表达式文法），仍由本类报出。</para>
/// </summary>
public static class UsharpParameterMatrixEmitter
{
    /// <summary>装配一个值型矩阵（<paramref name="space"/> 须为 <c>over=</c> 指向的空间声明）。</summary>
    public static UsharpMatrixBinding Emit(UsharpMatrixDecl decl, UsharpSpaceDecl space)
    {
        ArgumentNullException.ThrowIfNull(decl);
        ArgumentNullException.ThrowIfNull(space);

        var diagnostics = new List<UsharpDiagnostic>();
        var schema = UsharpParameterSchemaFactory.Build(space, diagnostics);
        var matrix = ParameterMatrix.For(schema);
        var rows = new List<UsharpMatrixRowBinding>(decl.Rows.Count);

        foreach (var row in decl.Rows)
        {
            if (!UsharpLinearExpression.TryParse(row.Expression, out var expression, out var error))
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.MatrixRowInvalid,
                    $"[Matrix] {decl.MatrixId}: 行 '{row.OutputKey}' 右式不可解析（{error}）；形如 bias + Σ weight·axis",
                    decl.SourceFile, decl.LineNumber));
                continue;
            }

            try
            {
                matrix.Row(row.OutputKey, r =>
                {
                    foreach (var term in expression.Terms) r.Plus(term.Axis, term.Weight);
                    if (expression.Bias != 0.0) r.Bias(expression.Bias);
                });
            }
            catch (ArgumentException)
            {
                // 装载期闸A 不通过：行不入绑定（US1003/US1010/US1004 的判据与归因**唯一权威 = UsharpValidator**，
                // 本类只做装配，不再第二次映射闸A 码 —— 同一缺陷只报一次）。
                continue;
            }

            rows.Add(new UsharpMatrixRowBinding(row.OutputKey, expression));
        }

        return new UsharpMatrixBinding(decl.MatrixId, UsharpMatrixKind.Parameter, space.SpaceId, rows,
            schema, matrix, null, diagnostics, decl.SourceFile, decl.LineNumber);
    }
}
