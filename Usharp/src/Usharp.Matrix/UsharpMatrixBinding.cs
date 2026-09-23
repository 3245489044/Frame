using BasicMethod.Declarative.Graph;
using BasicMethod.Declarative.Spec;
using Strategy.DecisionRules;
using Usharp.Semantics;

namespace Usharp.Matrix;

/// <summary>
/// 矩阵读法（U2 实例化两类；E1 增判据读法）。治理读法（<c>ModuleAxisMatrix</c>）仍待 U2b
/// —— 需 <c>kind=modules</c> 声明面，属语法扩张，须先有真实消费方（对齐「拒绝无校验的新前缀」）。
/// </summary>
public enum UsharpMatrixKind
{
    /// <summary>值域矩阵：<c>[Matrix] over=&lt;kind=values 空间&gt;</c> → <c>ParameterMatrix</c>（<c>x′ = Quantize(W·x+b)</c>）。</summary>
    Parameter,

    /// <summary>转移矩阵：<c>[Matrix] over=&lt;kind=keys 空间&gt;</c> → 步骤键转移表（<c>ChainSignalResolver</c> 的构造输入）。</summary>
    Transition,

    /// <summary>判据矩阵：<c>[Matrix] over=&lt;kind=terms 空间&gt;</c> → <c>DecisionGraph</c>（面内优先级 + 命中即停 + 吸收短路）。</summary>
    Decision,
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

    /// <summary>
    /// 判据读法的装配产物（<see cref="UsharpMatrixKind.Decision"/> 时非空；其余读法 null）。
    /// **加法式 init 属性**（不改位置参数）；类型归既有 <c>BasicMethod.Map.Declarative</c>（不新造裁决机制）。
    /// </summary>
    public DecisionGraph? DecisionGraph { get; init; }
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
