namespace Share.Signal;

/// <summary>
/// 信号回执终态（SCF SC-I1/SC-I5）：<c>Completed / Failed / Cancelled / Expired / DeadLetter</c>。
/// <para>由 <c>decisions/signal-ack.ushar</c> 判定表产出；C# 侧不写第二份映射。</para>
/// </summary>
public enum SignalOutcome
{
    /// <summary>成功终态。</summary>
    Completed = 0,

    /// <summary>失败（可重试预算内 → Queued；耗尽 → DeadLetter）。</summary>
    Failed = 1,

    /// <summary>外部取消。</summary>
    Cancelled = 2,

    /// <summary>TTL 到期。</summary>
    Expired = 3,

    /// <summary>重试耗尽。</summary>
    DeadLetter = 4,
}

/// <summary>回执终态 ↔ 信号状态映射（唯一映射点）。</summary>
public static class SignalOutcomes
{
    /// <summary>回执 → 状态。</summary>
    public static SignalState ToState(SignalOutcome outcome) => outcome switch
    {
        SignalOutcome.Completed => SignalState.Completed,
        SignalOutcome.Failed => SignalState.Failed,
        SignalOutcome.Cancelled => SignalState.Cancelled,
        SignalOutcome.Expired => SignalState.Expired,
        SignalOutcome.DeadLetter => SignalState.DeadLetter,
        _ => SignalState.Failed,
    };

    /// <summary>声明词 → 回执（判定表标签；未知 ⇒ null）。</summary>
    public static SignalOutcome? Parse(string? label) => label?.Trim().ToLowerInvariant() switch
    {
        "completed" => SignalOutcome.Completed,
        "failed" => SignalOutcome.Failed,
        "cancelled" => SignalOutcome.Cancelled,
        "expired" => SignalOutcome.Expired,
        "deadletter" => SignalOutcome.DeadLetter,
        _ => null,
    };
}
