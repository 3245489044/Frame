using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>声明能力闭合判据（启动期）：可空/条件端口必须出现在声明 outports 内且互斥。</summary>
public sealed class CapabilityDeclaredRule : IJudgmentRule
{
    public const string UnclosedCode = "decl.capability-unclosed";

    public string Name => "capability-declared";

    public int Specificity => 110;

    public JudgmentScope AppliesTo => JudgmentScope.Static;

    public IReadOnlyList<JudgmentFinding> Evaluate(JudgmentInput input)
    {
        var declared = new HashSet<string>(input.DeclaredPorts, StringComparer.Ordinal);
        var findings = new List<JudgmentFinding>();

        foreach (var port in input.Capability.NullablePorts ?? Array.Empty<string>())
        {
            if (declared.Contains(port)) continue;
            findings.Add(Finding(port, "nullable-not-declared"));
        }

        foreach (var port in input.Capability.ConditionalPorts ?? Array.Empty<string>())
        {
            if (declared.Contains(port)) continue;
            findings.Add(Finding(port, "conditional-not-declared"));
        }

        foreach (var port in input.Capability.NullablePorts ?? Array.Empty<string>())
        {
            if (input.Capability.IsConditional(port)) findings.Add(Finding(port, "nullable-and-conditional"));
        }

        return findings;
    }

    private JudgmentFinding Finding(string port, string reason)
        => new(
            Name,
            UnclosedCode,
            Verdict3.False,
            new[] { port },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = reason });
}
