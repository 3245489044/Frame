using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>声明外键判据（唯一实现）：实际键 − 声明键 − 豁免键。</summary>
public sealed class UndeclaredKeysRule : IJudgmentRule
{
    private readonly bool _inports;

    public UndeclaredKeysRule(string code, bool inports)
    {
        Code = code;
        _inports = inports;
    }

    public string Code { get; }

    public string Name => _inports ? "undeclared-inport" : "undeclared-outport";

    public int Specificity => 80;

    public JudgmentScope AppliesTo => JudgmentScope.Step | JudgmentScope.Round | JudgmentScope.Static;

    public IReadOnlyList<JudgmentFinding> Evaluate(JudgmentInput input)
    {
        var declared = new HashSet<string>(
            _inports ? input.DeclaredInports : input.DeclaredPorts, StringComparer.Ordinal);
        foreach (var key in input.ExemptKeys)
        {
            if (!string.IsNullOrWhiteSpace(key)) declared.Add(key);
        }

        var findings = new List<JudgmentFinding>();
        foreach (var key in _inports ? input.ActualInportKeys : input.ActualOutportKeys)
        {
            if (string.IsNullOrWhiteSpace(key) || declared.Contains(key)) continue;
            findings.Add(new JudgmentFinding(Name, Code, Verdict3.False, new[] { key }));
        }

        return findings;
    }
}
