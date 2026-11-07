using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Ledger;

/// <summary>判据输入状态（**空 / 未加载 / 未解析一律不得默认通过**）。</summary>
public enum CriterionInputState
{
    /// <summary>已装载且有标的。</summary>
    Loaded,

    /// <summary>已装载但集合为空（无标的 ⇒ 红）。</summary>
    Empty,

    /// <summary>未装载（不可判定 ⇒ 红）。</summary>
    Unloaded,

    /// <summary>已装载但解析失败（不可判定 ⇒ 红）。</summary>
    Unresolved,

    /// <summary>调用方**显式声明**不适用（唯一允许的跳过形态）。</summary>
    NotApplicableDeclared,

    /// <summary>把「跳过」当默认（隐式不适用 ⇒ 红）。</summary>
    NotApplicableSilent,
}

/// <summary>判据输入校验结论。</summary>
public enum CriterionVerdict
{
    /// <summary>可计入并视为通过。</summary>
    Accepted,

    /// <summary>红（空标的 / 隐式跳过）。</summary>
    Rejected,

    /// <summary>显式不适用（不计入、不判绿）。</summary>
    NotApplicable,

    /// <summary>不可判定（按红处理）。</summary>
    Unverifiable,
}

/// <summary>单条判据输入（事实 + 状态，不含判定逻辑）。</summary>
/// <param name="Name">判据名（如 <c>attached</c> / <c>errors</c> / <c>faces</c>）。</param>
/// <param name="State">输入状态。</param>
/// <param name="ItemCount">标的数量（<see cref="CriterionInputState.Loaded"/> 时有效）。</param>
/// <param name="Source">来源（诊断用）。</param>
public sealed record CriterionInput(
    string Name,
    CriterionInputState State,
    int ItemCount = 0,
    string? Source = null);

/// <summary>判据输入校验结果。</summary>
public sealed record CriterionValidation(
    string Name,
    CriterionVerdict Verdict,
    string Reason)
{
    /// <summary>是否通过并可计入。</summary>
    public bool IsAccepted => Verdict == CriterionVerdict.Accepted;
}

/// <summary>
/// 判据输入校验内核：把「空集合 / 未加载 / 解析失败 / 隐式跳过」显式化为红或显式不适用。
/// <para>动机：旧健康判定对空集求和 ⇒ 0 错误 ⇒ 全绿；本内核禁止「无数据 = 无问题」。</para>
/// </summary>
public static class CriterionInputValidator
{
    /// <summary>校验单条判据输入。</summary>
    public static CriterionValidation Validate(CriterionInput? input)
    {
        if (input is null)
            return new CriterionValidation("<null>", CriterionVerdict.Unverifiable, "判据输入为 null（不可判定 ⇒ 红）");

        var name = string.IsNullOrWhiteSpace(input.Name) ? "<unnamed>" : input.Name.Trim();
        return input.State switch
        {
            CriterionInputState.Loaded when input.ItemCount > 0 =>
                new CriterionValidation(name, CriterionVerdict.Accepted,
                    $"{name}: 已装载且标的 {input.ItemCount} 项"),
            CriterionInputState.Loaded =>
                new CriterionValidation(name, CriterionVerdict.Rejected,
                    $"{name}: 已装载但标的为空（空集不是「无需判定」⇒ 红）"),
            CriterionInputState.Empty =>
                new CriterionValidation(name, CriterionVerdict.Rejected,
                    $"{name}: 判据集合为空（无标的 ⇒ 红）"),
            CriterionInputState.Unloaded =>
                new CriterionValidation(name, CriterionVerdict.Unverifiable,
                    $"{name}: 未装载（不可判定 ⇒ 红）"),
            CriterionInputState.Unresolved =>
                new CriterionValidation(name, CriterionVerdict.Unverifiable,
                    $"{name}: 解析失败（不可判定 ⇒ 红）"),
            CriterionInputState.NotApplicableDeclared =>
                new CriterionValidation(name, CriterionVerdict.NotApplicable,
                    $"{name}: 显式声明不适用（不计入判定）"),
            CriterionInputState.NotApplicableSilent =>
                new CriterionValidation(name, CriterionVerdict.Rejected,
                    $"{name}: 隐式跳过（未显式声明不适用 ⇒ 红）"),
            _ => new CriterionValidation(name, CriterionVerdict.Unverifiable,
                $"{name}: 未知输入状态 '{input.State}'（不可判定 ⇒ 红）"),
        };
    }

    /// <summary>校验全部判据输入（空清单本身就是红）。</summary>
    public static IReadOnlyList<CriterionValidation> ValidateAll(IEnumerable<CriterionInput>? inputs)
    {
        if (inputs is null)
            return [new CriterionValidation("<inputs>", CriterionVerdict.Unverifiable, "未提供判据输入（不可判定 ⇒ 红）")];

        var validations = inputs.Select(Validate).ToArray();
        return validations.Length > 0
            ? validations
            : [new CriterionValidation("<inputs>", CriterionVerdict.Rejected, "判据输入为空（未提供任何判据 ⇒ 红）")];
    }
}

/// <summary>健康判定结论（**只有 <see cref="Green"/> 可放行**；<see cref="NotApplicable"/> 需显式声明）。</summary>
public enum HealthVerdict
{
    /// <summary>全部判据通过、无错误/警告、覆盖完整。</summary>
    Green,

    /// <summary>红：空集 / 未装载 / 缺计数 / 错误警告 / 对账不完整。</summary>
    Red,

    /// <summary>显式声明全部不适用（不放行，但也不假装通过）。</summary>
    NotApplicable,
}

/// <summary>健康判定策略（缺省即最严；放宽必须显式）。</summary>
/// <param name="WarningsBlock">警告是否阻断（缺省 true）。</param>
/// <param name="RequireReconciliation">是否要求多面对账报告（缺省 true）。</param>
/// <param name="RequiredPositiveCounters">必须为正的核心计数名（如 <c>attached</c>）。</param>
public sealed record HealthPolicy(
    bool WarningsBlock = true,
    bool RequireReconciliation = true,
    IReadOnlyList<string>? RequiredPositiveCounters = null);

/// <summary>
/// 健康判定证据（**每个字段都是事实，不允许由空集求和推导**）。
/// </summary>
/// <param name="Criteria">判据输入。</param>
/// <param name="ErrorCount">错误计数（来自完整的诊断聚合面，而非附着事实）。</param>
/// <param name="WarningCount">警告计数。</param>
/// <param name="Counters">关键计数（如 <c>attached</c> / <c>declared</c>）。</param>
/// <param name="Reconciliation">多面对账报告（可为 null，按策略判红）。</param>
/// <param name="DiagnosticsAggregated">诊断是否在完整面聚合过（false ⇒ 空集求和不可采信）。</param>
/// <param name="DiagnosticsScope">诊断聚合面（如 <c>declared</c>；用于审计「错误来自哪一面」）。</param>
public sealed record HealthEvidence(
    IReadOnlyList<CriterionInput>? Criteria,
    int ErrorCount,
    int WarningCount,
    IReadOnlyDictionary<string, int>? Counters = null,
    FaceReconciliationReport? Reconciliation = null,
    bool DiagnosticsAggregated = true,
    string? DiagnosticsScope = null);

/// <summary>健康判定结果。</summary>
public sealed record HealthAdjudicationResult(
    HealthVerdict Verdict,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> AcceptedCriteria)
{
    /// <summary>是否可放行（唯一绿口径）。</summary>
    public bool IsGreen => Verdict == HealthVerdict.Green;
}

/// <summary>
/// 健康判定内核（fail-closed）：
/// <list type="bullet">
/// <item>诊断未聚合 ⇒ 红（空集求和不得当 0 错误）。</item>
/// <item>错误/警告 &gt; 0 ⇒ 红；警告是否阻断由策略显式声明。</item>
/// <item>核心计数缺失 or ≤ 0（如 <c>attached=0</c>）⇒ 红。</item>
/// <item>判据输入空/未装载/未解析/隐式跳过 ⇒ 红；显式不适用才可排除。</item>
/// <item>多面对账未提供 / 不完整 ⇒ 红（除非策略显式不要求）。</item>
/// <item>只有 <see cref="HealthVerdict.Green"/> 放行。</item>
/// </list>
/// </summary>
public static class FailClosedHealthAdjudicator
{
    /// <summary>判定（缺省策略 = 最严）。</summary>
    public static HealthAdjudicationResult Adjudicate(HealthEvidence evidence, HealthPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        policy ??= new HealthPolicy();

        var reasons = new List<string>();
        var accepted = new List<string>();
        var notApplicable = 0;

        // 1) 诊断聚合面：未聚合 ⇒ 空集求和无意义。
        if (!evidence.DiagnosticsAggregated)
        {
            reasons.Add("诊断未在完整面聚合（不可由空集求和推导 0 错误 ⇒ 红）");
        }
        else if (evidence.ErrorCount < 0 || evidence.WarningCount < 0)
        {
            reasons.Add("错误/警告计数为负（不可判定 ⇒ 红）");
        }
        else
        {
            if (evidence.ErrorCount > 0)
                reasons.Add($"错误 {evidence.ErrorCount} 项（诊断面 {evidence.DiagnosticsScope ?? "<未声明>"}）");
            if (evidence.WarningCount > 0 && policy.WarningsBlock)
                reasons.Add($"警告 {evidence.WarningCount} 项（策略：警告阻断）");
        }

        // 2) 核心计数：缺失 or ≤ 0 一律红（attached=0 不能由「无错误」抵消）。
        var requiredCounters = policy.RequiredPositiveCounters ?? [];
        foreach (var counter in requiredCounters)
        {
            if (string.IsNullOrWhiteSpace(counter))
                continue;

            if (evidence.Counters is null || !evidence.Counters.TryGetValue(counter, out var value))
            {
                reasons.Add($"核心计数缺失 '{counter}'（不可判定 ⇒ 红）");
                continue;
            }

            if (value <= 0)
                reasons.Add($"核心计数 '{counter}' = {value}（须 > 0 ⇒ 红）");
        }

        // 3) 判据输入。
        foreach (var validation in CriterionInputValidator.ValidateAll(evidence.Criteria))
        {
            switch (validation.Verdict)
            {
                case CriterionVerdict.Accepted:
                    accepted.Add(validation.Name);
                    break;
                case CriterionVerdict.NotApplicable:
                    notApplicable++;
                    break;
                case CriterionVerdict.Rejected:
                case CriterionVerdict.Unverifiable:
                    reasons.Add(validation.Reason);
                    break;
            }
        }

        if (accepted.Count == 0 && notApplicable == 0)
            reasons.Add("无任何可计入判据（空判据面 ⇒ 红）");

        // 4) 多面对账。
        if (evidence.Reconciliation is null)
        {
            if (policy.RequireReconciliation)
                reasons.Add("未提供多面对账报告（声明面 ↔ 派发面 ↔ 装配面不可判定 ⇒ 红）");
        }
        else if (!evidence.Reconciliation.Ok)
        {
            reasons.AddRange(evidence.Reconciliation.Reasons.Count > 0
                ? evidence.Reconciliation.Reasons
                : [$"多面对账覆盖不完整（{evidence.Reconciliation.Coverage}）"]);
        }

        // 5) 结论：只有无红因且至少一条判据通过才绿；全部显式不适用 ⇒ NotApplicable。
        var verdict = reasons.Count > 0
            ? HealthVerdict.Red
            : accepted.Count > 0
                ? HealthVerdict.Green
                : HealthVerdict.NotApplicable;

        return new HealthAdjudicationResult(verdict, reasons, accepted);
    }
}
