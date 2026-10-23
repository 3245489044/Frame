using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>判定动作（唯一出口）：Skip = 合法；Report = 可见化；Throw = 唯一抛点。</summary>
public enum JudgmentAction
{
    Skip,
    Report,
    Throw,
}

public sealed record JudgmentOutcome(
    JudgmentAction Action,
    JudgmentVerdict Verdict,
    RoundFailure? Failure = null);

public static partial class JudgmentEngine
{
    /// <summary>
    /// 动作唯一出口：严格度在此生效，此时才允许 throw。内核 Judge 保持纯函数。
    /// </summary>
    public static JudgmentOutcome Apply(JudgmentVerdict verdict, RoundContractStrictness strictness)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        if (verdict.Verdict == Verdict3.True)
        {
            return new JudgmentOutcome(JudgmentAction.Skip, verdict);
        }

        if (strictness == RoundContractStrictness.Throw)
        {
            var violation = verdict.Violations[0];
            var failure = new RoundFailure(
                violation.Code,
                violation.Subjects.FirstOrDefault() ?? string.Empty,
                Array.Empty<string>(),
                Array.Empty<string>(),
                $"判定未通过: [{string.Join(",", verdict.Violations.Select(static v => v.Code))}]",
                violation.Subjects);

            throw new RoundFailureException(failure);
        }

        return new JudgmentOutcome(JudgmentAction.Report, verdict);
    }
}
