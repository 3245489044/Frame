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
/// 声明契约判据（E1/E2/E3/E4）**单点实现**的兼容入口：实现已迁至 <see cref="RoundContractJudge"/>，
/// 本类仅保留旧名转发一迭代（dep: replaced-by(RoundContractJudge)）。
/// </summary>
public static class DeclaredContractEnforcer
{
    /// <summary>声明外键 = 实际键 − 声明键 − 豁免键（内部键）。空 = 闭合。</summary>
    public static IReadOnlyList<string> Undeclared(
        IEnumerable<string>? declared,
        IEnumerable<string> actual,
        IReadOnlyCollection<string>? exempt = null)
        => RoundContractJudge.Undeclared(declared, actual, exempt);

    /// <summary>E1：声明步骤中既未执行、也未「合法跳过」的步骤（升序；空 = 闭合）。</summary>
    public static IReadOnlyList<string> MissingSteps(
        IEnumerable<string> declaredSteps,
        IEnumerable<string> executed,
        IEnumerable<string>? skipped = null)
        => RoundContractJudge.MissingSteps(declaredSteps, executed, skipped);
}