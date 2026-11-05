using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.View;

/// <summary>
/// 读面归因受控词（空因/降级因单源；禁调用点手写字面量）。
/// </summary>
public static class ReadReasonCodes
{
    /// <summary>读执行器未装配或开关关闭 ⇒ 已降级到旧读面。</summary>
    public const string EngineUnavailable = "engine-unavailable";

    /// <summary>执行器可用但未产出任何消息（「确实没有」≠ 读失败）。</summary>
    public const string EngineEmpty = "engine-empty";

    /// <summary>读计划未声明。</summary>
    public const string PlanMissing = "read-plan-missing";

    /// <summary>读源异常（读失败 ⇒ 必须可见化，禁压平为空）。</summary>
    public const string SourceFailed = "read-source-failed";

    /// <summary>端口未装配。</summary>
    public const string PortNotWired = "port-not-wired";
}

/// <summary>单读源贡献（域/条数/原因）。</summary>
public sealed record ReadSourceStat(string Domain, int Contributed, string? Reason)
{
    /// <summary>该源是否贡献了消息。</summary>
    public bool ContributedMessages => Contributed > 0;
}

/// <summary>
/// 读面结果信封（通用件）：终态、空因、逐源贡献、是否降级、读计划键。
/// <para>不变量：<see cref="Degraded"/> 为真 ⇒ <see cref="EmptyReason"/> 必为降级因；空结果必须带因。</para>
/// </summary>
public sealed record ReadOutcome(
    AnswerStatus Status,
    string? EmptyReason,
    IReadOnlyList<ReadSourceStat> Sources,
    bool Degraded,
    string PlanKey)
{
    /// <summary>执行器不可用 ⇒ 降级信封（调用方回旧读面，原因随行上送）。</summary>
    public static ReadOutcome DegradedToFallback(string planKey, string reason = ReadReasonCodes.EngineUnavailable)
        => new(AnswerStatus.Unknown, reason, Array.Empty<ReadSourceStat>(), Degraded: true, planKey);

    /// <summary>执行器可用但空 ⇒ 非降级空态（带因，禁切换真源）。</summary>
    public static ReadOutcome EmptyBySource(string planKey, string reason = ReadReasonCodes.EngineEmpty)
        => new(AnswerStatus.NoAnswer, reason, Array.Empty<ReadSourceStat>(), Degraded: false, planKey);

    /// <summary>由逐源贡献构造（空集 ⇒ 空因取首源原因，否则 <see cref="ReadReasonCodes.EngineEmpty"/>）。</summary>
    public static ReadOutcome FromSources(string planKey, AnswerStatus status, IReadOnlyList<ReadSourceStat>? sources)
    {
        var list = sources ?? Array.Empty<ReadSourceStat>();
        var contributed = list.Sum(s => Math.Max(0, s.Contributed));
        var reason = contributed > 0
            ? null
            : list.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Reason))?.Reason ?? ReadReasonCodes.EngineEmpty;
        return new ReadOutcome(status, reason, list, Degraded: false, planKey);
    }

    /// <summary>诊断文案（单点；调用方禁另写拼接）。</summary>
    public string Describe()
        => Degraded
            ? $"{PlanKey}: degraded（{EmptyReason}）"
            : EmptyReason is null
                ? $"{PlanKey}: {Status}"
                : $"{PlanKey}: {Status} / {EmptyReason}";
}

/// <summary>观测缺口类别（三态可分：无调用 ≠ 读失败 ≠ 未登记）。</summary>
public enum ObservationGapKind
{
    /// <summary>无缺口。</summary>
    None = 0,

    /// <summary>确实没有调用（空事实，非失败）。</summary>
    NoInvocation = 1,

    /// <summary>读失败（数据源异常/不可用）——禁压平为「无调用」。</summary>
    ReadFailed = 2,

    /// <summary>事实存在但登记缺失（仅影响补充字段，不改变行可见性）。</summary>
    NotRegistered = 3,
}

/// <summary>观测缺口（类别 + 原因；原因必填）。</summary>
public sealed record ObservationGap(ObservationGapKind Kind, string Reason)
{
    /// <summary>无缺口。</summary>
    public static ObservationGap None { get; } = new(ObservationGapKind.None, string.Empty);

    /// <summary>读失败缺口（原因取异常/端口诊断）。</summary>
    public static ObservationGap ReadFailed(string reason)
        => new(ObservationGapKind.ReadFailed, string.IsNullOrWhiteSpace(reason) ? ReadReasonCodes.SourceFailed : reason);

    /// <summary>确实无调用。</summary>
    public static ObservationGap NoInvocation(string reason)
        => new(ObservationGapKind.NoInvocation, string.IsNullOrWhiteSpace(reason) ? "no-invocation" : reason);

    /// <summary>是否构成故障（读失败）。</summary>
    public bool IsFailure => Kind == ObservationGapKind.ReadFailed;
}
