using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Ports;
using Share.Composition.Text;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>端口存在性判据（唯一实现）：显式 null 由可空声明豁免；缺席由条件产出声明豁免。</summary>
public sealed class PortPresenceRule : IJudgmentRule
{
    public string Name => "port-presence";

    public int Specificity => 100;

    public JudgmentScope AppliesTo => JudgmentScope.Step | JudgmentScope.Round | JudgmentScope.Static;

    public IReadOnlyList<JudgmentFinding> Evaluate(JudgmentInput input)
    {
        var findings = new List<JudgmentFinding>();
        foreach (var port in input.DeclaredPorts)
        {
            if (string.IsNullOrWhiteSpace(port)) continue;

            var presence = input.Fact.PresenceOf(port, input.Semantics);
            var state = presence.ToState();
            if (state == TextState.Known) continue;
            if (state == TextState.Empty && input.Capability.IsNullable(port)) continue;
            if (state == TextState.Unknown && input.Capability.IsConditional(port)) continue;

            findings.Add(new JudgmentFinding(
                Name,
                RoundFailureCodes.ExecPortMissing,
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
