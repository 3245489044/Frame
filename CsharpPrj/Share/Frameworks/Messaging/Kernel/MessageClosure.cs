using System.Text;

namespace Share.Messaging;

/// <summary>单条委托的观测状态（Predicted/Pending 不是完成）。</summary>
public enum MessageObservationState
{
    /// <summary>权威结果。</summary>
    Ready,

    /// <summary>失败终态。</summary>
    Failed,

    /// <summary>尚未落地（显式未完成）。</summary>
    Pending,

    /// <summary>仅预测（不是完成）。</summary>
    Predicted,

    /// <summary>过期。</summary>
    Expired,

    /// <summary>查无此槽（结构缺失）。</summary>
    Missing,
}

/// <summary>一条委托的观测结果（结构化；禁叙事替代）。</summary>
public sealed record MessageObservation(
    string CorrelationId,
    string MessageId,
    MessageObservationState State,
    string? Summary = null,
    string? Diagnostic = null,
    string? Payload = null)
{
    /// <summary>是否已闭环（Ready/Failed/Expired —— 与 <see cref="IsTerminal"/> 同口径，终态即闭环）。</summary>
    public bool IsClosed => IsTerminal;

    /// <summary>是否终态（Ready/Failed/Expired）。</summary>
    public bool IsTerminal => State is MessageObservationState.Ready or MessageObservationState.Failed
        or MessageObservationState.Expired;
}

/// <summary>一轮协同的闭环报告（未闭环必须可读）。</summary>
public sealed record MessageClosureReport(
    bool Closed,
    IReadOnlyList<MessageObservation> Observations,
    IReadOnlyList<string> Pending,
    IReadOnlyList<string> Failed,
    string Report)
{
    /// <summary>闭环且无失败。</summary>
    public bool Ok => Closed && Failed.Count == 0;

    /// <summary>空报告（无委托）。</summary>
    public static readonly MessageClosureReport Empty = new(
        true, Array.Empty<MessageObservation>(), Array.Empty<string>(), Array.Empty<string>(), string.Empty);
}

/// <summary>
/// 闭包判据（纯函数）：每条 Command 必须恰有一个终态 Result。
/// <para>缺失/未终态 ⇒ Closed=false，并输出未推进清单 + 逐项卡点。</para>
/// </summary>
public static class MessageClosure
{
    /// <summary>评估一组观测 → 闭环报告。</summary>
    public static MessageClosureReport Evaluate(IReadOnlyList<MessageObservation>? observations)
    {
        if (observations is null || observations.Count == 0) return MessageClosureReport.Empty;

        var pending = new List<string>();
        var failed = new List<string>();
        var seenPending = new HashSet<string>(StringComparer.Ordinal);
        var seenFailed = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < observations.Count; i++)
        {
            var item = observations[i];
            if (item is null || string.IsNullOrWhiteSpace(item.CorrelationId)) continue;

            if (item.State == MessageObservationState.Failed)
            {
                if (seenFailed.Add(item.CorrelationId)) failed.Add(item.CorrelationId);
                continue;
            }

            if (!item.IsClosed && seenPending.Add(item.CorrelationId)) pending.Add(item.CorrelationId);
        }

        var closed = pending.Count == 0;
        return new MessageClosureReport(
            closed, observations, pending, failed, Compose(observations, pending, failed));
    }

    /// <summary>单条观测是否构成闭环。</summary>
    public static bool IsClosed(MessageObservation? observation)
        => observation is not null && observation.IsClosed;

    /// <summary>信封 → 观测（状态映射：Acked→Ready、Failed→Failed、Expired→Expired、其余→Pending）。</summary>
    public static MessageObservation FromEnvelope(MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var state = envelope.State switch
        {
            MessageState.Acked => MessageObservationState.Ready,
            MessageState.Failed => MessageObservationState.Failed,
            MessageState.Expired => MessageObservationState.Expired,
            MessageState.DeadLetter => MessageObservationState.Failed,
            MessageState.Cancelled => MessageObservationState.Failed,
            _ => MessageObservationState.Pending,
        };

        return new MessageObservation(
            envelope.CorrelationId ?? envelope.MessageId,
            envelope.MessageId,
            state,
            envelope.Error,
            envelope.Error,
            envelope.Payload);
    }

    private static string Compose(
        IReadOnlyList<MessageObservation> observations,
        IReadOnlyList<string> pending,
        IReadOnlyList<string> failed)
    {
        if (pending.Count == 0 && failed.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        if (pending.Count > 0)
        {
            sb.Append("未闭环委托（").Append(pending.Count).Append("）：");
            sb.AppendLine(string.Join("；", Detail(observations, pending)));
        }

        if (failed.Count > 0)
        {
            sb.Append("失败委托（").Append(failed.Count).Append("）：");
            sb.AppendLine(string.Join("；", Detail(observations, failed)));
        }

        return sb.ToString().TrimEnd();
    }

    private static List<string> Detail(IReadOnlyList<MessageObservation> observations, IReadOnlyList<string> keys)
    {
        var result = new List<string>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var state = "Unknown";
            var diagnostic = (string?)null;
            for (var j = 0; j < observations.Count; j++)
            {
                if (!string.Equals(observations[j].CorrelationId, key, StringComparison.Ordinal)) continue;
                state = observations[j].State.ToString();
                diagnostic = observations[j].Diagnostic;
                break;
            }

            result.Add(string.IsNullOrWhiteSpace(diagnostic) ? $"{key}={state}" : $"{key}={state}({diagnostic})");
        }

        return result;
    }
}
