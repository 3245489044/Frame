using BasicMethod.Declarative.Spec;
using Strategy.DecisionRules;
using Usharp.Semantics;

namespace Usharp.Matrix;

/// <summary>
/// 矩阵读法（U2 只实例化两类）。判据读法（<c>DecisionGraph</c>）与治理读法（<c>ModuleAxisMatrix</c>）待 U2b
/// —— 前者需 <c>kind=terms</c> 词元轴与规则体语法，后者需 <c>BindKind/BindStatus</c> 声明面，均属语法扩张，须先有真实消费方（对齐「拒绝无校验的新前缀」）。
/// </summary>
public enum UsharpMatrixKind
{
    /// <summary>值域矩阵：<c>[Matrix] over=&lt;kind=values 空间&gt;</c> → <c>ParameterMatrix</c>（<c>x′ = Quantize(W·x+b)</c>）。</summary>
    Parameter,

    /// <summary>转移矩阵：<c>[Matrix] over=&lt;kind=keys 空间&gt;</c> → 步骤键转移表（<c>ChainSignalResolver</c> 的构造输入）。</summary>
    Transition,
}

/// <summary>矩阵一行的装配结果：输出坐标 + 解析后的线性表达式（引用坐标取 <see cref="UsharpLinearExpression.Axes"/>，不另存镜像字段）。</summary>
public sealed record UsharpMatrixRowBinding(
    string OutputKey,
    UsharpLinearExpression Expression);

/// <summary>
/// 一个 <c>[Matrix]</c> 声明的实例化产物：按读法携带 <see cref="ParameterMatrix"/> 或转移表。
/// <see cref="Diagnostics"/> 为该矩阵装配期的 Usharp 诊断（闸A 归因 / 行表达式非法 / 行形态非法）。
/// </summary>
public sealed record UsharpMatrixBinding(
    string MatrixId,
    UsharpMatrixKind Kind,
    string OverSpaceId,
    IReadOnlyList<UsharpMatrixRowBinding> Rows,
    ParameterSchema? Schema,
    ParameterMatrix? ParameterMatrix,
    IReadOnlyDictionary<string, string>? TransitionTable,
    IReadOnlyList<UsharpDiagnostic> Diagnostics,
    string SourceFile,
    int LineNumber)
{
    /// <summary>已装配行数（取数：与 <c>[Matrix]</c> 缩进块声明行数比对）。</summary>
    public int RowCount => Rows.Count;
}

/// <summary>一次矩阵装配的批产物（<see cref="UsharpMatrixEmitter"/> 输出；诊断与编译诊断分离，避免重复计入）。</summary>
public sealed record UsharpMatrixEmitResult(
    IReadOnlyList<UsharpMatrixBinding> Bindings,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>按矩阵号取装配产物。</summary>
    public bool TryGet(string matrixId, out UsharpMatrixBinding binding)
    {
        foreach (var b in Bindings)
        {
            if (!string.Equals(b.MatrixId, matrixId, StringComparison.Ordinal)) continue;
            binding = b;
            return true;
        }
        binding = null!;
        return false;
    }

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no matrix diagnostics)");
}
