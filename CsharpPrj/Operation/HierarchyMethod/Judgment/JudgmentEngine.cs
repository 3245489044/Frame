using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>
/// 判定引擎（纯函数）：过滤 AppliesTo → Specificity 降序 → 稳定序 → 逐条 Evaluate → 聚合。
/// 禁止在本类内写领域分支、IO、日志或 throw。
/// </summary>
public static partial class JudgmentEngine
{
    public static JudgmentVerdict Judge(
        JudgmentInput input,
        IReadOnlyList<IJudgmentRule>? rules = null,
        IJudgmentAuditSink? audit = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ordered = (rules ?? JudgmentRules.Default)
            .Where(rule => rule.AppliesTo.HasFlag(input.Scope))
            .OrderByDescending(static rule => rule.Specificity)
            .ThenBy(static rule => rule.Name, StringComparer.Ordinal)
            .ToArray();

        var findings = new List<JudgmentFinding>();
        foreach (var rule in ordered)
        {
            var produced = rule.Evaluate(input);
            if (produced is { Count: > 0 }) findings.AddRange(produced);
        }

        var verdict = JudgmentVerdict.From(findings);
        audit?.Record(new JudgmentAuditEntry(input.Scope, verdict.Verdict, findings));
        return verdict;
    }
}
