using System.Collections.Generic;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>诊断收集器（测试/观测装配用；线程内顺序即事件序）。</summary>
public sealed class RelationDiagnosticsCollector : IRelationDiagnostics
{
    private readonly List<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries;

    public void RelationRejected(string operation, string? edge, string reason)
        => _entries.Add($"rejected|{operation}|{edge}|{reason}");

    public void RelationDuplicate(string operation, string? edge, string reason)
        => _entries.Add($"duplicate|{operation}|{edge}|{reason}");

    public void RelationMissingEndpoint(string operation, string? edge, string endpoint)
        => _entries.Add($"missing-endpoint|{operation}|{edge}|{endpoint}");

    public void RelationProbeUnassembled(string operation, string? edge, string reason)
        => _entries.Add($"probe-unassembled|{operation}|{edge}|{reason}");

    public void RelationProbeUnmapped(string operation, string? edge, string endpoints)
        => _entries.Add($"probe-unmapped|{operation}|{edge}|{endpoints}");

    public void RelationInvariantViolation(string operation, string invariant, string reason)
        => _entries.Add($"invariant|{operation}|{invariant}|{reason}");

    public void RelationPlanMissing(string reason)
        => _entries.Add($"plan-missing|{reason}");

    public void RelationRevived(string edge, string sourceKey, string targetKey, string? tombstoneReason)
        => _entries.Add($"revived|{edge}|{sourceKey}->{targetKey}|{tombstoneReason ?? string.Empty}");

    public void RelationWriteSkipped(string operation, string? edge, string verdict, string role, string key)
        => _entries.Add($"write-skipped|{operation}|{edge}|{verdict}|{role}|{key}");

    public void RelationWriteNotApplicable(string operation, string? edge, string reason, string role, string key)
        => _entries.Add($"write-not-applicable|{operation}|{edge}|{reason}|{role}|{key}");

    public void RelationWriteUnavailable(string operation, string? edge, string code, string role, string key)
        => _entries.Add($"write-unavailable|{operation}|{edge}|{code}|{role}|{key}");
}
