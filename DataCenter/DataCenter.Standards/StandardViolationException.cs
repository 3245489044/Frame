using System.Text.RegularExpressions;
using BasicMethod.Declarative.Ledger;

namespace DataCenter.Standards;

/// <summary>
/// 标准一致性违规（校验失败即**阻断**，不 warn）——对齐上位方案「契约即断言」：
/// 声明漂移必须当场可见（设计文档 §3.4 纪律）。
/// <para>
/// 类型身份保持不变（消费侧既有 <c>catch</c> 面不改）：机制宿主在框架，异常基类为
/// <see cref="DeclarationViolationException"/>。
/// </para>
/// <para>
/// 诊断口径（B7 归一）：装载期同一失败**只报一次**，且明细逐条带判据号（<c>C1</c>–<c>C14</c>）；
/// <see cref="Criteria"/> 为判据号的**类型化视图**（按首现序去重），供守卫与宿主按判据号分流，
/// 免去字符串再解析（文案本身逐字不动）。
/// </para>
/// </summary>
public sealed class StandardViolationException : DeclarationViolationException
{
    /// <summary>判据号前缀形态（<c>C7 组合族跨项引用…</c>；由 <c>LedgerConsistency.Check</c> 补齐）。</summary>
    private static readonly Regex CriterionPrefix = new(@"^(C\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>单条违规（无明细时为空）。</summary>
    public StandardViolationException(string message) : base(message)
        => Criteria = Extract(message, Violations);

    /// <summary>批量违规（多判据一次报全，便于一次修数据）。</summary>
    public StandardViolationException(string message, IReadOnlyList<string> violations) : base(message, violations)
        => Criteria = Extract(message, Violations);

    /// <summary>本异常涉及的判据号（按首现序去重；明细无编号时退回消息首部扫描）。</summary>
    public IReadOnlyList<string> Criteria { get; }

    /// <summary>判据号提取：明细逐条取前缀；无明细时扫描消息（仅取首个编号，防把 <c>C1–C14</c> 区段文案误读）。</summary>
    private static IReadOnlyList<string> Extract(string message, IReadOnlyList<string> violations)
    {
        if (violations.Count == 0)
        {
            var single = CriterionPrefix.Match(message.TrimStart());
            return single.Success ? [single.Groups[1].Value] : [];
        }

        var codes = new List<string>();
        foreach (var violation in violations)
        {
            var match = CriterionPrefix.Match(violation.TrimStart());
            if (match.Success && !codes.Contains(match.Groups[1].Value, StringComparer.Ordinal))
                codes.Add(match.Groups[1].Value);
        }

        return codes;
    }
}
