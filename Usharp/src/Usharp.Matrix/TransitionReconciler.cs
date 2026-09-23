using Usharp.Semantics;

namespace Usharp.Matrix;

/// <summary>
/// 转移表**声明一致性对账**（通用）：把「<c>[Matrix]</c> 显式转移表」与「<c>[Flow] step</c> 派生链」逐键比对，
/// 输出分歧清单与「矩阵独有键」（= 链入口静态化，见下），供消费方可见化（矩阵为权威，派生链为对照）。
/// <para>
/// 口径：同键不同值 / 派生表有而矩阵缺 ⇒ 分歧；矩阵独有键 = 链入口（如 <c>start</c>），合法且不超一条时静默。
/// </para>
/// </summary>
public static class TransitionReconciler
{
    /// <summary>
    /// 按 <c>over=</c> 空间号读显式转移表（判据 = 转移矩阵且 <c>OverSpaceId</c> 逐字等于
    /// <paramref name="overSpaceId"/>；多个命中取首个并回报 <see cref="TransitionTableReadResult.Matched"/>）。
    /// </summary>
    /// <param name="compilation">一次编译（矩阵装配面自此取，不二次编译）。</param>
    /// <param name="overSpaceId">目标空间号（<c>[Flow] over=</c>）。</param>
    public static TransitionTableReadResult ReadOverSpace(UsharpCompilation compilation, string overSpaceId)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (string.IsNullOrWhiteSpace(overSpaceId)) return TransitionTableReadResult.None;

        IReadOnlyDictionary<string, string>? table = null;
        var matched = 0;
        foreach (var binding in UsharpMatrixEmitter.Emit(compilation).Bindings)
        {
            if (binding.Kind != UsharpMatrixKind.Transition) continue;
            if (!string.Equals(binding.OverSpaceId, overSpaceId, StringComparison.Ordinal)) continue;
            if (binding.TransitionTable is not { Count: > 0 } candidate) continue;

            matched++;
            table ??= candidate;
        }

        return new TransitionTableReadResult(table, matched, overSpaceId);
    }

    /// <summary>
    /// 逐键对账：<paramref name="declared"/>（矩阵，权威）与 <paramref name="flowDerived"/>（流步骤序派生）。
    /// 返回分歧清单（<c>{键}:(矩阵缺)</c> / <c>{键}:矩阵={x}/流={y}</c>）与矩阵独有键序。
    /// </summary>
    /// <param name="declared">显式声明转移表（权威）。</param>
    /// <param name="flowDerived">流步骤序派生转移表（对照）。</param>
    public static TransitionReconcileReport Reconcile(
        IReadOnlyDictionary<string, string>? declared,
        IReadOnlyDictionary<string, string>? flowDerived)
    {
        if (declared is null || flowDerived is null) return TransitionReconcileReport.Empty;

        var divergences = new List<string>();
        foreach (var pair in flowDerived)
        {
            if (!declared.TryGetValue(pair.Key, out var next))
                divergences.Add($"{pair.Key}:(矩阵缺)");
            else if (!string.Equals(next, pair.Value, StringComparison.Ordinal))
                divergences.Add($"{pair.Key}:矩阵={next}/流={pair.Value}");
        }

        var entryKeys = declared.Keys.Where(k => !flowDerived.ContainsKey(k)).ToList();
        return new TransitionReconcileReport(divergences, entryKeys);
    }
}

/// <summary>
/// 对账结果：<see cref="Divergences"/>（同键分歧 / 矩阵缺键）、<see cref="EntryKeys"/>（矩阵独有键 = 链入口）。
/// <see cref="IsDivergent"/> = 有分歧 ∨ 链入口多于一条（多于一条即非「单入口静态化」，须可见化）。
/// </summary>
public sealed record TransitionReconcileReport(
    IReadOnlyList<string> Divergences,
    IReadOnlyList<string> EntryKeys)
{
    /// <summary>空结果（任一侧无表 ⇒ 无可对账）。</summary>
    public static TransitionReconcileReport Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    /// <summary>是否需要可见化（分歧 > 0 ∨ 链入口 > 1）。</summary>
    public bool IsDivergent => Divergences.Count > 0 || EntryKeys.Count > 1;
}
