using System.Collections.Generic;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>单条判定发现（码 + 三值 + 主体 + 结构事实）。</summary>
public sealed record JudgmentFinding(
    string RuleId,
    string Code,
    Verdict3 Verdict,
    IReadOnlyList<string> Subjects,
    IReadOnlyDictionary<string, string>? Facts = null);
