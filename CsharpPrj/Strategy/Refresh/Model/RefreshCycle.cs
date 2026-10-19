using System;
using System.Collections.Generic;

namespace Strategy.Refresh.Model
{
    /// <summary>一次刷新周期（L0 契约）：触发来源 + 版本 + 范围 + 追踪号。</summary>
    public sealed record RefreshCycle(
        string SlotKey,
        long Version,
        RefreshTrigger Trigger,
        IReadOnlyList<string>? Scopes,
        string? TraceId,
        DateTimeOffset StartedAt);

    /// <summary>一次刷新周期的结果（L0 契约）：框架唯一的执行反馈。</summary>
    public sealed record RefreshCycleResult(
        string SlotKey,
        long Version,
        RefreshTrigger Trigger,
        RefreshOutcome Outcome,
        AcceptDecision Decision,
        bool Delivered,
        bool ContentChanged,
        bool Merged,
        string Fingerprint,
        TimeSpan Duration,
        object? Payload = null,
        Exception? Error = null);
}
