using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>链闭合结果：记录集 × 声明步 × 合法跳过（判据载体 = 步名集合）。</summary>
public sealed record ChainClosure(
    bool Closed,
    IReadOnlyList<string> MissingSteps,
    IReadOnlyList<string> UnexpectedSteps,
    string? FirstUnexecuted)
{
    public static ChainClosure Open { get; } = new(false, Array.Empty<string>(), Array.Empty<string>(), null);
}

/// <summary>链闭合纯判据（吸收后端各自的 skipped/计数推导，报数单源）。</summary>
public static class RoundChainClosure
{
    public static ChainClosure Evaluate(
        RoundSpec spec,
        IReadOnlyList<NodeExecutionRecord> records,
        IReadOnlyList<string>? skipped = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(records);

        var declared = spec.Steps.Select(static s => s.EffectiveNodeId).ToArray();
        var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
        var executed = records.Select(static r => r.NodeKey)
            .Where(static k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var missing = RoundContractJudge.MissingSteps(declared, executed, skipped);
        var unexpected = executed.Where(k => !declaredSet.Contains(k))
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();

        return new ChainClosure(
            missing.Count == 0,
            missing,
            unexpected,
            missing.Count == 0 ? null : missing[0]);
    }
}
