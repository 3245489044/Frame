using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>执行契约严格度（F5）：<c>Report</c> = 违规可见化不中断；<c>Throw</c> = 违规即抛。</summary>
public enum RoundContractStrictness
{
    /// <summary>只可见化（默认；迁移期不误杀存量流）。</summary>
    Report,

    /// <summary>违规即抛（生产姿态）。</summary>
    Throw,
}

/// <summary>
/// 轮执行契约选项（F5）：由 <see cref="NodeRoundContext"/> 携带，取代进程级静态开关
/// （静态开关无法按装配姿态/测试隔离）。
/// </summary>
/// <param name="Strictness">严格度（默认 Report）。</param>
/// <param name="InternalKeys">实现内部键（非端口）：E2 判据据此不误报 KeyMapping 补填键。</param>
public sealed record RoundContractOptions(
    RoundContractStrictness Strictness = RoundContractStrictness.Report,
    IReadOnlyCollection<string>? InternalKeys = null)
{
    /// <summary>默认选项（Report + 空豁免）。</summary>
    public static RoundContractOptions Default { get; } = new();
}

/// <summary>
/// 声明契约判据（E1 步次闭合 / E2 入参越界 / E3 出参越界）**单点实现**：纯函数、无领域词。
/// <para>框架侧（<see cref="NodeGraphOrchestrator"/>）与 Agent 侧（执行器）共用本实现，禁各自再写一份
/// （SingleImplementation）。判据载体 = 键集合（结构事实），不含执行者叙述。</para>
/// </summary>
public static class DeclaredContractEnforcer
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

    /// <summary>
    /// E1：声明步骤中既未执行、也未「合法跳过」的步骤（升序；空 = 闭合）。
    /// 合法跳过 = 编排器登记的 <paramref name="skipped"/>（条件门控未执行），不误报。
    /// </summary>
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
}