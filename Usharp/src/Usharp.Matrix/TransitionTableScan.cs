namespace Usharp.Matrix;

/// <summary>
/// 转移表扫描**单一实现**（D-9）：按谓词扫一次矩阵装配批产物，返回表 / 命中数 / <c>over=</c> 空间号。
/// 消费方：<see cref="TransitionGate.ReadTransitions"/> 与 <see cref="TransitionReconciler.ReadOverSpace"/>。
/// </summary>
internal static class TransitionTableScan
{
    /// <summary>扫描转移类绑定。</summary>
    /// <param name="bindings">矩阵装配批产物。</param>
    /// <param name="predicate">命中判据（矩阵号 / 空间号等）。</param>
    public static TransitionTableReadResult Scan(
        IEnumerable<UsharpMatrixBinding> bindings, Func<UsharpMatrixBinding, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(predicate);

        IReadOnlyDictionary<string, string>? table = null;
        var matched = 0;
        string? overSpaceId = null;

        foreach (var binding in bindings)
        {
            if (binding.Kind != UsharpMatrixKind.Transition) continue;
            if (!predicate(binding)) continue;

            matched++;
            overSpaceId ??= binding.OverSpaceId;
            if (binding.TransitionTable is { Count: > 0 } candidate) table ??= candidate;
        }

        return new TransitionTableReadResult(table, matched, overSpaceId);
    }
}