using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// <c>[Matrix]</c> 装配门面：按 <c>over=</c> 指向空间的值类分派读法（<c>values</c> → 值域矩阵 <c>ParameterMatrix</c>；
/// <c>keys</c> → 转移表）。
/// <para>不接入 <see cref="UsharpCompilation"/>：核心门面保持零矩阵实例依赖，矩阵装配为显式消费
/// （避免同一失败在编译诊断与装配诊断两处重复计入，也避免把 Strategy 重量传导进语言核心）。</para>
/// </summary>
public static class UsharpMatrixEmitter
{
    /// <summary>装配一次编译中的全部 <c>[Matrix]</c>（<c>over</c> 空间缺失者跳过 —— 已由 <see cref="UsharpValidator"/> 报 US1001）。</summary>
    public static UsharpMatrixEmitResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Emit(compilation.Matrices, compilation.Spaces);
    }

    /// <summary>装配给定矩阵声明集（空间判据来自 <paramref name="spaces"/>，与闸A 同一真源）。</summary>
    public static UsharpMatrixEmitResult Emit(IReadOnlyList<UsharpMatrixDecl> matrices, SpaceRegistry spaces)
    {
        ArgumentNullException.ThrowIfNull(matrices);
        ArgumentNullException.ThrowIfNull(spaces);

        var bindings = new List<UsharpMatrixBinding>(matrices.Count);
        var diagnostics = new List<UsharpDiagnostic>();

        foreach (var decl in matrices)
        {
            var over = decl.OverSpaceId;
            if (string.IsNullOrWhiteSpace(over) || !spaces.TryGet(over!, out var space)) continue;

            var binding = space.Kind == UsharpSpaceKind.Values
                ? UsharpParameterMatrixEmitter.Emit(decl, space)
                : UsharpTransitionMatrixEmitter.Emit(decl, space);

            bindings.Add(binding);
            diagnostics.AddRange(binding.Diagnostics);
        }

        return new UsharpMatrixEmitResult(bindings, diagnostics);
    }
}
