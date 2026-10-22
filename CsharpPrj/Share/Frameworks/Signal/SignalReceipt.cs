namespace Share.Signal;

/// <summary>
/// 投递回执（SCF：投递 ≠ 成功；只证明已受理/已发出）。
/// </summary>
public readonly record struct SignalReceipt(
    SignalId Id,
    string LeaseId,
    SignalState State,
    string Reason)
{
    /// <summary>是否被受理（Accepted/Dispatched；不代表目标完成）。</summary>
    public bool Accepted => State is SignalState.Accepted or SignalState.Dispatched;

    /// <summary>是否去重命中（未二次发）。</summary>
    public bool Duplicate => string.Equals(Reason, "duplicate", System.StringComparison.OrdinalIgnoreCase);
}
