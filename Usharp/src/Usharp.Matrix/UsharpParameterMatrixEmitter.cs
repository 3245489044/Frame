using BasicMethod.Declarative.Spec;
using Strategy.DecisionRules;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// 值域矩阵发射器：<c>[Space] kind=values</c> → <see cref="ParameterSchema"/>；
/// <c>[Matrix] over=&lt;值型空间&gt;</c> → <see cref="ParameterMatrix"/>（整向量一次变换 <c>x′ = Quantize(W·x+b)</c>）。
/// <para>闸A 唯一权威 = <see cref="ParameterMatrix.Row"/>（行内每个引用必须已在 schema 声明，否则装载期即抛）；
/// 本类的异常归因只为把失败报成带 <c>文件:行</c> 的 Usharp 诊断，不构成第二份判据。</para>
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

            rows.Add(new UsharpMatrixRowBinding(row.OutputKey, expression, expression.Axes));

            try
            {
                matrix.Row(row.OutputKey, r =>
                {
                    foreach (var term in expression.Terms) r.Plus(term.Axis, term.Weight);
                    if (expression.Bias != 0.0) r.Bias(expression.Bias);
                });
            }
            catch (ArgumentException ex)
            {
                // 装载期闸A 失败：按「输出坐标未声明」或「行内引用未声明」归因到既有闸A 诊断码
                var code = schema.TryGet(row.OutputKey) is null
                    ? UsharpCodes.MatrixRowOutUndeclared
                    : UsharpCodes.MatrixRowInputUndeclared;
                diagnostics.Add(UsharpDiagnostic.Error(code,
                    $"[Matrix] {decl.MatrixId}: 行 '{row.OutputKey}' 未通过装载期闸A（ParameterMatrix.Row）—— {ex.Message}",
                    decl.SourceFile, decl.LineNumber));
            }
        }

        return new UsharpMatrixBinding(decl.MatrixId, UsharpMatrixKind.Parameter, space.SpaceId, rows,
            schema, matrix, null, diagnostics, decl.SourceFile, decl.LineNumber);
    }
}
