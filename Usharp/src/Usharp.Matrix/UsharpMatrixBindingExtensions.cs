using System;
using System.Collections.Generic;

namespace Usharp.Matrix;

/// <summary>
/// 转移表读取的**唯一省略式**：消费方只关心「表」而不关心歧义计数时，用本扩展代替
/// 逐处手写的 <c>TryGet + TransitionTable is { Count: &gt; 0 } + 空表回落</c>。
/// <para>判据单源保持 = <see cref="TransitionGate.ReadTransitions"/>（内部走 <c>TransitionTableScan</c>，D-9）；
/// 需要歧义可见化的调用方仍直接用 <see cref="TransitionGate.ReadTransitions"/>。</para>
/// </summary>
public static class UsharpMatrixBindingExtensions
{
    /// <summary>按矩阵号取转移表（未装配 / 空白号 / 表空 ⇒ 空表，调用方无需判空）。</summary>
    public static IReadOnlyDictionary<string, string> TransitionTableOrEmpty(
        this UsharpMatrixEmitResult? emit, string matrixId)
        => TransitionGate.ReadTransitions(emit, matrixId).Table
           ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
