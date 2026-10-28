namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 写边结构结果（唯一判据载体）：区分「真写 / 策略跳过 / 策略拒绝」，禁止把 Skip/Deny 复述为成功。
/// <para>权威：<c>AX.NoSilentOutlet</c>（静默出口禁入）·<c>axioms:AX.BlastRadiusNonExemption</c>（必要性不读影响面）。</para>
/// </summary>
/// <param name="Verdict">写入门裁决（四态）。</param>
/// <param name="Wrote">是否真的落库（false = 未写，调用方不得计成功）。</param>
/// <param name="TombstoneCleared">是否因本次写入解除了墓碑（显式复活证据）。</param>
/// <param name="Reason">裁决原因原文（跳过/复活均带；空 = 无障碍）。</param>
public readonly record struct GraphWriteOutcome(
    GraphWriteVerdict Verdict,
    bool Wrote,
    bool TombstoneCleared = false,
    string? Reason = null)
{
    /// <summary>本次写入是否被策略拦下（跳过/拒绝）。</summary>
    public bool Skipped => !Wrote;

    /// <summary>已写入（可选带复活证据）。</summary>
    public static GraphWriteOutcome Written(GraphWriteVerdict verdict, bool tombstoneCleared, string? reason = null)
        => new(verdict, true, tombstoneCleared, reason);

    /// <summary>未写入（策略跳过/拒绝；原因必填，禁静默）。</summary>
    public static GraphWriteOutcome Blocked(GraphWriteVerdict verdict, string reason)
        => new(verdict, false, false, reason);
}
