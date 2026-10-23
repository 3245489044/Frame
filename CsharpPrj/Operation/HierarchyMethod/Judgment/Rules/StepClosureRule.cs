using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>步次闭合判据（唯一实现）：声明步骤中既未执行、也未合法跳过者 = 缺失。</summary>
public sealed class StepClosureRule : IJudgmentRule
{
    public string Name => "step-closure";

    public int Specificity => 90;

    public JudgmentScope AppliesTo => JudgmentScope.Round;

    public IReadOnlyList<JudgmentFinding> Evaluate(JudgmentInput input)
    {
        var covered = new HashSet<string>(input.ExecutedSteps, StringComparer.Ordinal);
        foreach (var step in input.SkippedSteps)
        {
            if (!string.IsNullOrWhiteSpace(step)) covered.Add(step);
        }

        var findings = new List<JudgmentFinding>();
        foreach (var step in input.DeclaredSteps)
        {
            if (string.IsNullOrWhiteSpace(step) || covered.Contains(step)) continue;
            findings.Add(new JudgmentFinding(
                Name,
                RoundFailureCodes.ExecStepMissing,
                Verdict3.False,
                new[] { step }));
        }

        return findings;
    }
}
