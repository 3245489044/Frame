using System.Collections.Generic;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>审计端口（投影面注入：日志/UI/落库），内核零 IO。</summary>
public interface IJudgmentAuditSink
{
    void Record(JudgmentAuditEntry entry);
}

public sealed record JudgmentAuditEntry(
    JudgmentScope Scope,
    Verdict3 Verdict,
    IReadOnlyList<JudgmentFinding> Findings);
