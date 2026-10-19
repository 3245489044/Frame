using System;

namespace Strategy.Refresh.Model
{
    /// <summary>每槽诊断快照（L0 契约）：只在状态迁移时产出，不在每拍刷屏。</summary>
    public sealed record RefreshSlotDiagnostics(
        string SlotKey,
        RefreshTrigger LastTrigger,
        RefreshOutcome LastOutcome,
        long LastVersion,
        string? LastProgressToken,
        int ConsecutiveNoChange,
        int ConsecutiveDeliveryFailures,
        TimeSpan CurrentDelay,
        int ActiveLeaseCount,
        DateTimeOffset? LastChangedAt,
        DateTimeOffset? NoProgressSince,
        long Pushes,
        long UnchangedDeliveries,
        long SuppressedHeartbeats);
}
