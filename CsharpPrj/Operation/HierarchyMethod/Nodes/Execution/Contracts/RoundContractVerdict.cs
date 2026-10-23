using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Judgment;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 契约判定结果（步次/入参/出参统一形状）：判据载体 = 键集合与步名集合（结构事实）。
/// </summary>
public sealed record RoundContractVerdict(
    IReadOnlyList<string> MissingSteps,
    IReadOnlyList<string> MissingPorts,
    IReadOnlyList<string> UndeclaredInports,
    IReadOnlyList<string> UndeclaredOutports)
{
    public static RoundContractVerdict Closed { get; } = new(
        System.Array.Empty<string>(), System.Array.Empty<string>(),
        System.Array.Empty<string>(), System.Array.Empty<string>());

    public bool HasViolations =>
        MissingSteps.Count > 0 || MissingPorts.Count > 0 ||
        UndeclaredInports.Count > 0 || UndeclaredOutports.Count > 0;

    /// <summary>违规项扁平文本（观测/异常消息用）。</summary>
    public IReadOnlyList<string> ToViolations()
    {
        var list = new List<string>(MissingSteps.Count + MissingPorts.Count + UndeclaredInports.Count + UndeclaredOutports.Count);
        foreach (var s in MissingSteps) list.Add($"{RoundFailureCodes.ExecStepMissing}@{s}");
        foreach (var p in MissingPorts) list.Add($"{RoundFailureCodes.ExecPortMissing}@{p}");
        foreach (var k in UndeclaredInports) list.Add($"{RoundFailureCodes.ExecUndeclaredInport}:{k}");
        foreach (var k in UndeclaredOutports) list.Add($"{RoundFailureCodes.ExecUndeclaredOutport}:{k}");
        return list;
    }

    /// <summary>投影为判定内核结论（动作出口消费；不产生第二套判定）。</summary>
    public JudgmentVerdict ToJudgmentVerdict()
    {
        var findings = new List<JudgmentFinding>(MissingSteps.Count + MissingPorts.Count + UndeclaredInports.Count + UndeclaredOutports.Count);
        findings.AddRange(MissingSteps.Select(static s => new JudgmentFinding(
            "round-contract", RoundFailureCodes.ExecStepMissing, Verdict3.False, new[] { s })));
        findings.AddRange(MissingPorts.Select(static p => new JudgmentFinding(
            "round-contract", RoundFailureCodes.ExecPortMissing, Verdict3.False, new[] { p })));
        findings.AddRange(UndeclaredInports.Select(static k => new JudgmentFinding(
            "round-contract", RoundFailureCodes.ExecUndeclaredInport, Verdict3.False, new[] { k })));
        findings.AddRange(UndeclaredOutports.Select(static k => new JudgmentFinding(
            "round-contract", RoundFailureCodes.ExecUndeclaredOutport, Verdict3.False, new[] { k })));
        return JudgmentVerdict.From(findings);
    }
}

