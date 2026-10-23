using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>判定结论聚合：⊥ 吸收（任一不可判定 ⇒ 整体不可判定），否则任一 False ⇒ False。</summary>
public sealed record JudgmentVerdict(IReadOnlyList<JudgmentFinding> Findings)
{
    public static JudgmentVerdict Empty { get; } = new(Array.Empty<JudgmentFinding>());

    public Verdict3 Verdict
    {
        get
        {
            if (Findings.Any(static f => f.Verdict == Verdict3.Undetermined)) return Verdict3.Undetermined;
            if (Findings.Any(static f => f.Verdict == Verdict3.False)) return Verdict3.False;
            return Verdict3.True;
        }
    }

    public bool HasViolations => Findings.Any(static f => f.Verdict != Verdict3.True);

    public IReadOnlyList<JudgmentFinding> Violations
        => Findings.Where(static f => f.Verdict != Verdict3.True).ToArray();

    public IReadOnlyList<string> SubjectsOf(string code)
        => Findings
            .Where(f => string.Equals(f.Code, code, StringComparison.Ordinal))
            .SelectMany(static f => f.Subjects)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToArray();

    public static JudgmentVerdict From(IEnumerable<JudgmentFinding> findings)
        => new(findings.ToArray());
}
