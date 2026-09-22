using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// 转移矩阵发射器：<c>[Space] kind=keys</c> + <c>[Matrix] over=&lt;键型空间&gt;</c>，行形态 <c>next = cur</c>
/// ⇒ 「已完成步骤键 → 下一步骤键」转移表（<c>ChainSignalResolver</c> 的构造输入，对齐声明式链 <c>on_success</c> 语义）。
/// <para>实例化 <c>ChainSignalResolver&lt;TState,TVertex&gt;</c> 需泛型实参 ⇒ 由消费方（U3 Emit 层）承担，本层只产转移表数据
/// （避免把 LoopFoldMethod 重量带进本层）。坐标存在性闸A 由 <see cref="UsharpValidator"/> 统一裁决，此处仅做行形态归因。</para>
/// </summary>
public static class UsharpTransitionMatrixEmitter
{
    /// <summary>装配一个键型（转移）矩阵。</summary>
    public static UsharpMatrixBinding Emit(UsharpMatrixDecl decl, UsharpSpaceDecl space)
    {
        ArgumentNullException.ThrowIfNull(decl);
        ArgumentNullException.ThrowIfNull(space);

        var diagnostics = new List<UsharpDiagnostic>();
        var rows = new List<UsharpMatrixRowBinding>(decl.Rows.Count);
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in decl.Rows)
        {
            var isTransitionShape = UsharpLinearExpression.TryParse(row.Expression, out var expression, out var error)
                && expression.Terms.Count == 1
                && expression.Bias == 0.0
                && expression.Terms[0].Weight == 1.0;

            if (!isTransitionShape)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.MatrixRowInvalid,
                    $"[Matrix] {decl.MatrixId}: 行 '{row.OutputKey}' 非转移形态（须为 `next = cur`：单一坐标引用、权重 1、无偏置）"
                    + (error is null ? string.Empty : $"；{error}"),
                    decl.SourceFile, decl.LineNumber));
                continue;
            }

            rows.Add(new UsharpMatrixRowBinding(row.OutputKey, expression));
            table[row.OutputKey] = expression.Terms[0].Axis;
        }

        return new UsharpMatrixBinding(decl.MatrixId, UsharpMatrixKind.Transition, space.SpaceId, rows,
            null, null, table, diagnostics, decl.SourceFile, decl.LineNumber);
    }
}
