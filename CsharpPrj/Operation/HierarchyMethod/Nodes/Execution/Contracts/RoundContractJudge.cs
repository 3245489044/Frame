using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 声明契约判据（E1 步次闭合 / E2 入参越界 / E3 出参越界 / E4 声明端口缺失）**单点实现**：纯函数、无领域词。
/// <para>判据载体 = 键集合 / 步名集合（结构事实），不含执行者叙述；框架与 Agent 侧共用本实现（SingleImplementation）。</para>
/// </summary>
public static class RoundContractJudge
{
    /// <summary>声明外键 = 实际键 − 声明键 − 豁免键（内部键）。空 = 闭合。</summary>
    public static IReadOnlyList<string> Undeclared(
        IEnumerable<string>? declared,
        IEnumerable<string> actual,
        IReadOnlyCollection<string>? exempt = null)
    {
        if (declared is null) return Array.Empty<string>();

        var allowed = new HashSet<string>(
            declared.Where(static k => !string.IsNullOrWhiteSpace(k)), StringComparer.Ordinal);
        if (exempt is not null)
        {
            foreach (var key in exempt)
            {
                if (!string.IsNullOrWhiteSpace(key)) allowed.Add(key);
            }
        }

        return actual
            .Where(static k => !string.IsNullOrWhiteSpace(k))
            .Where(k => !allowed.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>E1：声明步骤中既未执行、也未「合法跳过」的步骤（升序；空 = 闭合）。</summary>
    public static IReadOnlyList<string> MissingSteps(
        IEnumerable<string> declaredSteps,
        IEnumerable<string> executed,
        IEnumerable<string>? skipped = null)
    {
        var covered = new HashSet<string>(executed ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (skipped is not null)
        {
            foreach (var step in skipped)
            {
                if (!string.IsNullOrWhiteSpace(step)) covered.Add(step);
            }
        }

        return declaredSteps
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Where(s => !covered.Contains(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>E4：声明 outport 中未实际产出的端口（空 = 闭合）。</summary>
    /// <param name="declaredPorts">声明产出端口。</param>
    /// <param name="produced">实际产出端口。</param>
    /// <param name="nullablePorts">允许显式 null 的端口（缺省 null = null 亦算缺失，零回归）；非空集合内的端口只在「键不存在」时算缺失（E7）。</param>
    public static IReadOnlyList<string> MissingPorts(
        IEnumerable<string>? declaredPorts, IReadOnlyDictionary<string, object?> produced,
        IReadOnlyCollection<string>? nullablePorts = null)
    {
        ArgumentNullException.ThrowIfNull(produced);
        if (declaredPorts is null) return Array.Empty<string>();

        HashSet<string>? nullable = nullablePorts is { Count: > 0 }
            ? new HashSet<string>(nullablePorts.Where(static p => !string.IsNullOrWhiteSpace(p)), StringComparer.Ordinal)
            : null;

        return declaredPorts
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Where(p => !produced.TryGetValue(p, out var v)
                        || (v is null && (nullable is null || !nullable.Contains(p))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 全量判定：<paramref name="judgeSteps"/>=false 时只判端口（失败轮不判步次，禁以缺步掩盖真实错误）。
    /// </summary>
    public static RoundContractVerdict Judge(
        RoundSpec spec,
        IReadOnlyList<NodeExecutionRecord> records,
        IEnumerable<string> executed,
        IReadOnlyList<string>? skipped,
        RoundContractOptions? options = null,
        bool judgeSteps = true)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(records);

        var opts = options ?? RoundContractOptions.Default;
        var declared = spec.Steps.Select(static s => s.EffectiveNodeId).ToArray();
        var executedKeys = new HashSet<string>(executed ?? Array.Empty<string>(), StringComparer.Ordinal);
        foreach (var record in records) executedKeys.Add(record.NodeKey);

        var missingSteps = judgeSteps
            ? MissingSteps(declared, executedKeys, skipped)
            : Array.Empty<string>();

        var byId = spec.Steps.ToDictionary(static s => s.EffectiveNodeId, StringComparer.Ordinal);
        var missingPorts = new List<string>();
        var undeclaredIn = new List<string>();
        var undeclaredOut = new List<string>();

        foreach (var record in records)
        {
            if (!byId.TryGetValue(record.NodeKey, out var step)) continue;

            foreach (var key in MissingPorts(step.ResultKeys, record.OutputPorts))
                missingPorts.Add($"{step.EffectiveNodeId}:[{key}]");

            if (record.InjectedKeys is { } injected)
            {
                foreach (var key in Undeclared(step.ParameterKeys, injected, opts.InternalKeys))
                    undeclaredIn.Add($"{step.EffectiveNodeId}:[{key}]");
            }

            foreach (var key in Undeclared(step.ResultKeys, record.OutputPorts.Keys, opts.InternalKeys))
                undeclaredOut.Add($"{step.EffectiveNodeId}:[{key}]");
        }

        return new RoundContractVerdict(
            missingSteps, missingPorts,
            undeclaredIn.Distinct(StringComparer.Ordinal).OrderBy(static x => x, StringComparer.Ordinal).ToArray(),
            undeclaredOut.Distinct(StringComparer.Ordinal).OrderBy(static x => x, StringComparer.Ordinal).ToArray());
    }
}
