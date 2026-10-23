using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>声明↔实现闭合违例（判据载体 = 结构坐标：规则 / 主体 / 明细）。</summary>
public sealed record ClosureViolation(string Rule, string Subject, string Detail);

/// <summary>闭合姿态：Report = 报告可见化；Throw = 启动失败。</summary>
public enum ClosurePosture
{
    Report,
    Throw,
}

/// <summary>闭合报告（报告项 ∩ 姿态 ⇒ 是否阻断）。</summary>
public sealed record ClosureReport(
    IReadOnlyList<ClosureViolation> Violations,
    ClosurePosture Posture)
{
    public bool Closed => Violations.Count == 0;

    public IReadOnlyList<string> Subjects =>
        Violations.Select(static v => v.Subject).Distinct(StringComparer.Ordinal).ToArray();
}

/// <summary>
/// 声明↔实现闭合原语（通用）：单条规则只关心「声明集合 vs 实现集合」，报告与姿态由 <see cref="ClosureGate"/> 裁决。
/// </summary>
public interface IClosureRule<in TDecl, in TImpl>
{
    IEnumerable<ClosureViolation> Check(
        IReadOnlyCollection<TDecl> declared,
        IReadOnlyCollection<TImpl> implemented);
}

/// <summary>闭合裁决（Report/Throw 单点；给结构坐标，不只报「装配失败」）。</summary>
public static class ClosureGate
{
    public const string RuleUnclosed = "decl.closure-unclosed";

    public static ClosureReport Evaluate(
        IEnumerable<ClosureViolation> violations,
        ClosurePosture posture = ClosurePosture.Report)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var list = violations.ToArray();
        var report = new ClosureReport(list, posture);
        if (posture == ClosurePosture.Throw && list.Length > 0)
        {
            throw new InvalidOperationException(
                $"{RuleUnclosed}: [{string.Join("; ", list.Select(v => $"{v.Rule}/{v.Subject}:{v.Detail}"))}]");
        }

        return report;
    }

    /// <summary>集合差集闭合：声明的键未在实现面登记 ⇒ 违例（按主体给出结构坐标）。</summary>
    public static IReadOnlyList<ClosureViolation> MissingSubjects<T>(
        IEnumerable<T> declared,
        IEnumerable<T> implemented,
        Func<T, string> nameOf,
        string rule = RuleUnclosed)
    {
        var set = new HashSet<T>(implemented);
        return declared.Where(d => !set.Contains(d))
            .Select(d => new ClosureViolation(rule, nameOf(d), "declared-not-implemented"))
            .ToArray();
    }
}
