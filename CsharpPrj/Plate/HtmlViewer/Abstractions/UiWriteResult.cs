#nullable enable

using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>写回结果状态（协调器单点裁决产物；类型化，替代裸 bool / 哨兵串）。</summary>
public enum UiWriteStatus
{
    /// <summary>无写回动作（Skip）。</summary>
    Nothing = 0,
    /// <summary>全部目标已生效。</summary>
    Applied,
    /// <summary>部分目标生效（逐目标独立，不回退整体）。</summary>
    Partial,
    /// <summary>全部目标失败。</summary>
    Failed,
    /// <summary>具名目标不存在（区域缺失；可定向自愈，区别于通用 Failed）。</summary>
    NotFound,
    /// <summary>键控对齐内核未就绪（可补注入自愈，区别于区域缺失）。</summary>
    NotReady,
    /// <summary>按指纹/内容去重跳过。</summary>
    Skipped,
}

/// <summary>写回结果（协调器单点裁决；Written/Skipped = 具名目标，Changed = DOM 变更计数，Fallback = 显式回退动作）。
/// <paramref name="Anomalies"/> = 键控对齐异常（如 duplicate-key-old/new），全链透传供日志/门禁可见化（不静默）。
/// <para><paramref name="UnchangedTargets"/>/<paramref name="FailedTargets"/> 把「未变」与「失败」从 <paramref name="Skipped"/>
/// 中单列出来——<paramref name="Skipped"/> 仅作为「未触 DOM」的兼容聚合面（未变 ∪ 失败）。</para></summary>
public sealed record UiWriteResult(
    UiWriteStatus Status,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Skipped,
    int Changed = 0,
    string? Fallback = null,
    string? TraceId = null,
    IReadOnlyList<string>? Anomalies = null,
    IReadOnlyList<string>? UnchangedTargets = null,
    IReadOnlyList<string>? FailedTargets = null);
