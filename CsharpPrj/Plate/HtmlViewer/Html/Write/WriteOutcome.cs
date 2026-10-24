#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>写回结果代数（四值 + 空）：Applied / Unchanged / Partial / Failed / Nothing。</summary>
public enum WriteOutcomeKind
{
    /// <summary>无目标（空计划）。</summary>
    Nothing = 0,
    /// <summary>全部目标落地成功，且无对齐异常。</summary>
    Applied,
    /// <summary>全部目标内容未变（去重跳过），不触 DOM。</summary>
    Unchanged,
    /// <summary>部分落地：存在失败或对齐异常，但同时有成功目标。</summary>
    Partial,
    /// <summary>无任何目标落地。</summary>
    Failed,
}

/// <summary>
/// 单目标终态（F6）：每 (target, generation) 恰一个终态；禁止同目标既 Committed 又 Skipped。
/// </summary>
public enum TargetOutcomeKind
{
    Committed = 0,
    Superseded,
    Failed,
    Skipped,
}

/// <summary>单目标终态记录（Code = 可复算原因：unchanged / stale / write-failed / budget-dropped）。</summary>
public sealed record TargetOutcome(string Target, TargetOutcomeKind Kind, string? Code = null);

/// <summary>
/// 结果代数（F6）：把 applied/unchanged/failed/anomalies 归一为每目标唯一终态。
/// 同一目标跨类别出现 ⇒ 记 <see cref="TargetOutcomeKind.Failed"/>（code=conflicting-outcome），
/// 使「同拍 Committed→Skip」成为可判定事实而非不可判。
/// </summary>
public static class TargetOutcomeAlgebra
{
    /// <summary>四类计数 → 每目标唯一终态；输出顺序 = 输入序（稳定，遥测/断言可按索引复算）。</summary>
    public static IReadOnlyList<TargetOutcome> Classify(
        IReadOnlyList<string> applied,
        IReadOnlyList<string> unchanged,
        IReadOnlyList<string> failed,
        IReadOnlyList<string>? anomalies = null)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var outcomes = new List<TargetOutcome>(applied.Count + unchanged.Count + failed.Count);

        foreach (var name in applied) Add(name, TargetOutcomeKind.Committed, null);
        foreach (var name in unchanged) Add(name, TargetOutcomeKind.Skipped, "unchanged");
        foreach (var name in failed) Add(name, TargetOutcomeKind.Failed, "write-failed");
        if (anomalies is not null)
        {
            foreach (var name in anomalies)
            {
                if (index.ContainsKey(name)) continue;
                Add(name, TargetOutcomeKind.Superseded, "alignment-anomaly");
            }
        }

        return outcomes;

        void Add(string? name, TargetOutcomeKind kind, string? code)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (index.TryGetValue(name, out var at))
            {
                // 同目标跨类别 ⇒ 终态不唯一，显式记为失败（禁后写覆盖前写）。
                if (outcomes[at].Kind != kind)
                    outcomes[at] = new TargetOutcome(name, TargetOutcomeKind.Failed, "conflicting-outcome");
                return;
            }
            index[name] = outcomes.Count;
            outcomes.Add(new TargetOutcome(name, kind, code));
        }
    }

    /// <summary>
    /// 结果种类唯一判据：只读每目标终态（禁再读 applied/unchanged/failed 计数，禁第二套聚合）。
    /// <para>判据：有 Failed/Superseded ⇒ <c>committed&gt;0 ? Partial : Failed</c>；否则 <c>committed&gt;0 ? Applied : (skipped&gt;0 ? Unchanged : Nothing)</c>。</para>
    /// </summary>
    public static WriteOutcomeKind KindOf(IReadOnlyList<TargetOutcome> outcomes)
    {
        if (outcomes is null || outcomes.Count == 0) return WriteOutcomeKind.Nothing;

        var committed = 0;
        var skipped = 0;
        var failed = 0;
        var superseded = 0;
        foreach (var outcome in outcomes)
        {
            switch (outcome.Kind)
            {
                case TargetOutcomeKind.Committed: committed++; break;
                case TargetOutcomeKind.Skipped: skipped++; break;
                case TargetOutcomeKind.Failed: failed++; break;
                default: superseded++; break;
            }
        }

        if (failed > 0 || superseded > 0)
            return committed > 0 ? WriteOutcomeKind.Partial : WriteOutcomeKind.Failed;
        if (committed > 0) return WriteOutcomeKind.Applied;
        return skipped > 0 ? WriteOutcomeKind.Unchanged : WriteOutcomeKind.Nothing;
    }
}

/// <summary>
/// 写回结果裁决（单一入口）：只按结构事实（applied / unchanged / failed / anomalies 计数）判定，
/// 禁以"有 written 即成功/有 skipped 即部分"手写聚合。
/// <para>判据：<c>Partial ⇔ (failed&gt;0 ∨ anomalies≠∅) ∧ applied&gt;0</c>；<c>Unchanged ⇔ applied=0 ∧ failed=0</c>。</para>
/// </summary>
public readonly record struct WriteOutcome(
    WriteOutcomeKind Kind,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string>? Anomalies)
{
    /// <summary>每目标唯一终态（F6；同拍同目标终态不唯一 ⇒ Failed/conflicting-outcome）。</summary>
    public IReadOnlyList<TargetOutcome> TargetOutcomes
        => TargetOutcomeAlgebra.Classify(Applied, Unchanged, Failed, Anomalies);

    /// <summary>唯一裁决点：由每目标终态反推结果种类（不另写计数判据，禁第二套聚合）。</summary>
    public static WriteOutcome Classify(
        IReadOnlyList<string> applied,
        IReadOnlyList<string> unchanged,
        IReadOnlyList<string> failed,
        IReadOnlyList<string>? anomalies = null)
    {
        var outcomes = TargetOutcomeAlgebra.Classify(applied, unchanged, failed, anomalies);
        return new WriteOutcome(
            TargetOutcomeAlgebra.KindOf(outcomes), applied, unchanged, failed, anomalies);
    }

    /// <summary>映射为对外状态（Skipped = 未触 DOM；FailedTargets 另见 <see cref="ToResult"/>）。</summary>
    public UiWriteStatus Status => Kind switch
    {
        WriteOutcomeKind.Applied => UiWriteStatus.Applied,
        WriteOutcomeKind.Unchanged => UiWriteStatus.Skipped,
        WriteOutcomeKind.Partial => UiWriteStatus.Partial,
        WriteOutcomeKind.Failed => UiWriteStatus.Failed,
        _ => UiWriteStatus.Nothing,
    };

    /// <summary>产出对外结果（Skipped 兼容面 = 未变 ∪ 失败；Unchanged/FailedTargets 精确面单列）。</summary>
    public UiWriteResult ToResult(string? traceId)
    {
        var notWritten = new List<string>(Unchanged.Count + Failed.Count);
        notWritten.AddRange(Unchanged);
        notWritten.AddRange(Failed);
        return new UiWriteResult(
            Status, Applied, notWritten, TraceId: traceId, Anomalies: Anomalies,
            UnchangedTargets: Unchanged, FailedTargets: Failed);
    }
}
