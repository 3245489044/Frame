using System;

namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 每槽反馈状态（L0 契约）：框架据此做心跳退避 / 熔断 / 诊断。
    /// <para><see cref="NextDelay"/> 是下一次该槽允许刷新的基础间隔；<see cref="NoProgressSince"/> 是连续无进展起点。</para>
    /// </summary>
    public sealed record SlotRefreshState(
        long LastDeliveredVersion,
        string LastFingerprint,
        DateTimeOffset LastChangedAt,
        DateTimeOffset LastAttemptAt,
        int ConsecutiveNoChange,
        int ConsecutiveDeliveryFailures,
        RefreshOutcome LastOutcome,
        TimeSpan NextDelay,
        string? LastProgressToken,
        DateTimeOffset? NoProgressSince)
    {
        /// <summary>未观测槽的初始状态。</summary>
        public static SlotRefreshState Empty { get; } = new(
            long.MinValue,
            string.Empty,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            0,
            0,
            RefreshOutcome.Unchanged,
            TimeSpan.FromMilliseconds(RefreshContract.TickMs),
            null,
            null);
    }
}
