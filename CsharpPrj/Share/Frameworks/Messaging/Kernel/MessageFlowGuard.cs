using System.Globalization;

namespace Share.Messaging;

/// <summary>流控判定（Ok=false 时 <see cref="Code"/> 为诊断码）。</summary>
public readonly record struct MessageFlowVerdict(bool Ok, string Code, string Detail)
{
    /// <summary>通过。</summary>
    public static MessageFlowVerdict Pass => new(true, MessageDiagnostics.Ok, string.Empty);

    /// <summary>拒绝。</summary>
    public static MessageFlowVerdict Reject(string code, string detail)
        => new(false, string.IsNullOrWhiteSpace(code) ? MessageDiagnostics.Unknown : code, detail ?? string.Empty);
}

/// <summary>
/// 流控判据（纯算术/纯字符串）：hop / fanout / TTL / queue / scope。
/// <para>时钟一律由参数传入（内核不取系统时钟）。</para>
/// </summary>
public static class MessageFlowGuard
{
    /// <summary>综合校验（按 scope → hop → fanout → TTL → queue 顺序短路）。</summary>
    public static MessageFlowVerdict Validate(
        MessageEnvelope message,
        IMessagePolicy policy,
        DateTimeOffset nowUtc,
        int queueDepth = 0,
        int fanoutTargets = 1)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(policy);

        var scope = CheckScope(message, policy);
        if (!scope.Ok) return scope;

        var hop = CheckHop(message, policy);
        if (!hop.Ok) return hop;

        var fanout = CheckFanout(fanoutTargets, policy);
        if (!fanout.Ok) return fanout;

        // TTL 判据自带截止时刻：缺省 DeadlineUtc 时在此按 TtlSeconds/策略缺省补齐，
        // 不依赖调用方是否先调 ComputeDeadlineUtc（否则 TTL 会静默失效）。
        var effective = string.IsNullOrWhiteSpace(message.DeadlineUtc)
            ? message with { DeadlineUtc = ComputeDeadlineUtc(message, policy, nowUtc) }
            : message;
        var ttl = CheckTtl(effective, nowUtc);
        if (!ttl.Ok) return ttl;

        return CheckQueue(queueDepth, policy);
    }

    /// <summary>隔离判据：Scope 空或越权 ⇒ 拒绝。</summary>
    public static MessageFlowVerdict CheckScope(MessageEnvelope message, IMessagePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(message.Scope))
            return MessageFlowVerdict.Reject(MessageDiagnostics.ScopeDenied, "Scope 为空（禁跨域投递）。");

        return policy.IsScopeAuthorized(message)
            ? MessageFlowVerdict.Pass
            : MessageFlowVerdict.Reject(MessageDiagnostics.ScopeDenied, $"Scope 越权：{message.Scope}。");
    }

    /// <summary>深度判据：Hop ≥ MaxHop ⇒ 死信。</summary>
    public static MessageFlowVerdict CheckHop(MessageEnvelope message, IMessagePolicy policy)
        => message.Hop < policy.MaxHop
            ? MessageFlowVerdict.Pass
            : MessageFlowVerdict.Reject(MessageDiagnostics.HopExceeded,
                $"Hop={message.Hop} ≥ MaxHop={policy.MaxHop}。");

    /// <summary>扇出判据：目标数 &gt; MaxFanout ⇒ 拒绝。</summary>
    public static MessageFlowVerdict CheckFanout(int targets, IMessagePolicy policy)
        => targets <= policy.MaxFanout
            ? MessageFlowVerdict.Pass
            : MessageFlowVerdict.Reject(MessageDiagnostics.FanoutExceeded,
                $"Targets={targets} > MaxFanout={policy.MaxFanout}。");

    /// <summary>TTL 判据：DeadlineUtc 已过 ⇒ 过期。</summary>
    public static MessageFlowVerdict CheckTtl(MessageEnvelope message, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(message.DeadlineUtc)) return MessageFlowVerdict.Pass;

        if (!DateTimeOffset.TryParse(message.DeadlineUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var deadline))
            return MessageFlowVerdict.Reject(MessageDiagnostics.Unsupported,
                $"DeadlineUtc 非法：{message.DeadlineUtc}（须 ISO8601）。");

        return deadline > nowUtc
            ? MessageFlowVerdict.Pass
            : MessageFlowVerdict.Reject(MessageDiagnostics.Expired,
                $"Deadline={deadline:O} ≤ now={nowUtc:O}。");
    }

    /// <summary>队列判据：深度 &gt; MaxQueueDepth ⇒ 拒绝（Command 死信；Event 由策略合并/丢弃并计数）。</summary>
    public static MessageFlowVerdict CheckQueue(int depth, IMessagePolicy policy)
        => depth <= policy.MaxQueueDepth
            ? MessageFlowVerdict.Pass
            : MessageFlowVerdict.Reject(MessageDiagnostics.QueueOverflow,
                $"QueueDepth={depth} > MaxQueueDepth={policy.MaxQueueDepth}。");

    /// <summary>计算截止时刻（ISO8601；显式 deadline 优先，其次 TTL，最后策略缺省）。</summary>
    public static string ComputeDeadlineUtc(MessageEnvelope message, IMessagePolicy policy, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(message.DeadlineUtc)) return message.DeadlineUtc!;
        var ttl = message.TtlSeconds ?? policy.DefaultTtlSeconds;
        return nowUtc.AddSeconds(ttl).ToString("O", CultureInfo.InvariantCulture);
    }
}
