namespace Share.Signal;

/// <summary>
/// 投递回执（SCF：投递 ≠ 成功；只证明已受理/已发出）。
/// </summary>
public readonly record struct SignalReceipt(
    SignalId Id,
    string LeaseId,
    SignalState State,
    string Reason,
    bool IsDuplicate = false)
{
    /// <summary>是否被受理（Accepted/Dispatched；不代表目标完成）。</summary>
    public bool Accepted => State is SignalState.Accepted or SignalState.Dispatched;

    /// <summary>是否去重命中（接纳结果显式标记，或 reason 为 duplicate）。</summary>
    public bool Duplicate => IsDuplicate || string.Equals(Reason, "duplicate", System.StringComparison.OrdinalIgnoreCase);
}
