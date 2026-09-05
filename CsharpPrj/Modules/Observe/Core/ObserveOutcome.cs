using System;

namespace Modules.Observe;

/// <summary>一次写操作的可观测结果：HighestAck 为实测最高 ack 级别；Window 为实测收敛窗口（Applied→Converged 时长）；Result=Committed 已收敛 / Degraded 显式降级 / Failed 失败。</summary>
public sealed record ObserveOutcome(
    string ChangeId,
    string Key,
    long Version,
    AckLevel HighestAck,
    TimeSpan Window,
    ChangeResult Result);
