namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 词表覆盖率对账（「幽灵词」判据）：词表里每个词都须有**消费点**（读取分支 / 执行分支），
/// 反之消费点引用的词都须在**词表**内。
/// <para>
/// 动机：结构门放行而执行器无分支 = **假阳性放行** ——「声明面列了词」不等于「实现面认得词」。
/// 本件把该缺口变成可断言事实：<see cref="GhostWords"/> 报「列了没实现」，
/// <see cref="UnknownWords"/> 报「实现了没登记」。
/// </para>
/// <para>
/// 机制半零领域词：词表与「消费点集合」全部由消费方注入（消费点取值不透明）。
/// </para>
/// </summary>
public static class LedgerVocabularyCoverage
{
    /// <summary>词表中**无消费点**的词（幽灵词；保持词表序）。</summary>
    public static IReadOnlyList<string> GhostWords(LedgerTerm term, IEnumerable<string> consumed)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(consumed);

        var known = new HashSet<string>(consumed, StringComparer.Ordinal);
        return [.. term.Words.Where(word => !known.Contains(word))];
    }

    /// <summary>消费点里**未登记**的词（漂移词；保持出现序、去重）。</summary>
    public static IReadOnlyList<string> UnknownWords(LedgerTerm term, IEnumerable<string> consumed)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(consumed);

        var declared = new HashSet<string>(term.Words, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var word in consumed)
        {
            if (word is null || declared.Contains(word) || !seen.Add(word)) continue;

            result.Add(word);
        }

        return result;
    }

    /// <summary>
    /// 双侧对账并追加违规（<paramref name="ghostMessage"/>/<paramref name="unknownMessage"/> 各取一词 → 文案）；
    /// 返回**本次调用**是否全绿（既有违规不影响返回值）。
    /// </summary>
    public static bool EnsureCovered(
        LedgerTerm term,
        IEnumerable<string> consumed,
        Func<string, string> ghostMessage,
        Func<string, string> unknownMessage,
        List<string> violations)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(consumed);
        ArgumentNullException.ThrowIfNull(ghostMessage);
        ArgumentNullException.ThrowIfNull(unknownMessage);
        ArgumentNullException.ThrowIfNull(violations);

        var materialized = consumed as IReadOnlyCollection<string> ?? [.. consumed];
        var before = violations.Count;

        foreach (var word in GhostWords(term, materialized)) violations.Add(ghostMessage(word));
        foreach (var word in UnknownWords(term, materialized)) violations.Add(unknownMessage(word));

        return violations.Count == before;
    }
}
