namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 判据宿主：跑全部判据 → 汇总 → 一次报全（阻断，不 warn）。
/// <para>
/// 失败文案由消费方经 <c>messageFactory</c> 注入（域文案留消费方）；缺省给通用文案。
/// </para>
/// </summary>
public static class LedgerConsistency
{
    /// <summary>跑全部判据，返回全部违规（空 = 全绿）。</summary>
    public static IReadOnlyList<string> Check<TCatalog>(TCatalog catalog, IEnumerable<ILedgerRule<TCatalog>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var violations = new List<string>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            var scratch = new List<string>();
            rule.Check(catalog, scratch);
            foreach (var violation in scratch)
            {
                if (string.IsNullOrEmpty(violation)) continue;

                // 判据编号补齐：文案已自带编号则不重复前缀（保证既有文案逐字不漂移）
                violations.Add(rule.Code is { Length: > 0 } code && !violation.StartsWith(code, StringComparison.Ordinal)
                    ? $"{code} {violation}"
                    : violation);
            }
        }

        return violations;
    }

    /// <summary>跑全部判据，有违规即抛（一次报全，便于一次修数据）。</summary>
    public static void EnsureValid<TCatalog>(
        TCatalog catalog,
        IEnumerable<ILedgerRule<TCatalog>> rules,
        Func<IReadOnlyList<string>, string>? messageFactory = null)
    {
        var violations = Check(catalog, rules);
        if (violations.Count == 0) return;

        var message = messageFactory?.Invoke(violations)
            ?? $"契约台账校验失败（{violations.Count} 条）："
               + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations);

        throw new DeclarationViolationException(message, violations);
    }
}
