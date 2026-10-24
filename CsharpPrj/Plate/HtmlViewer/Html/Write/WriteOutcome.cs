#nullable enable

using System;
using System.Collections.Generic;
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
    /// <summary>唯一裁决点：由目标分类计数推出结果种类。</summary>
    public static WriteOutcome Classify(
        IReadOnlyList<string> applied,
        IReadOnlyList<string> unchanged,
        IReadOnlyList<string> failed,
        IReadOnlyList<string>? anomalies = null)
    {
        var hasAnomaly = anomalies is { Count: > 0 };
        WriteOutcomeKind kind;
        if (applied.Count == 0 && failed.Count == 0 && !hasAnomaly)
            kind = unchanged.Count == 0 ? WriteOutcomeKind.Nothing : WriteOutcomeKind.Unchanged;
        else if (failed.Count == 0 && !hasAnomaly)
            kind = WriteOutcomeKind.Applied;
        else if (applied.Count == 0)
            kind = WriteOutcomeKind.Failed;
        else
            kind = WriteOutcomeKind.Partial;
        return new WriteOutcome(kind, applied, unchanged, failed, anomalies);
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
