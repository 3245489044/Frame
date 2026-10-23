using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Ports;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>提示词前置判据（唯一实现）：必填端口缺失时，若别名来源有值 ⇒ 派生成功。</summary>
public sealed class AliasCoverageRule : IJudgmentRule
{
    public const string DerivedCode = "prompt.derived";
    public const string MissingCode = "prompt_missing";

    public string Name => "alias-coverage";

    public int Specificity => 100;

    public JudgmentScope AppliesTo => JudgmentScope.Preflight;

    public IReadOnlyList<JudgmentFinding> Evaluate(JudgmentInput input)
    {
        var findings = new List<JudgmentFinding>();
        foreach (var port in input.DeclaredPorts)
        {
            if (string.IsNullOrWhiteSpace(port)) continue;

            var presence = input.Fact.PresenceOf(port, input.Semantics);
            if (presence == PortPresence.Value) continue;

            var source = input.Capability.AliasesOf(port)
                .FirstOrDefault(alias => input.Fact.PresenceOf(alias, input.Semantics) == PortPresence.Value);

            if (source is not null)
            {
                findings.Add(new JudgmentFinding(
                    Name,
                    DerivedCode,
                    Verdict3.True,
                    new[] { port },
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = source }));
                continue;
            }

            findings.Add(new JudgmentFinding(
                Name,
                MissingCode,
                Verdict3.False,
                new[] { port },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["presence"] = presence.ToString(),
                }));
        }

        return findings;
    }
}
