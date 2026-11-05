namespace Modules.Threading;

/// <summary>
/// 卡死取证：慢路径自我报告的**只读进度快照**（无锁共享，跨线程安全）。
/// 纪律：仅承载不可变值（阶段/资源/持锁者 + 单调 Tick），禁放可写状态句柄，
/// 对齐 <see cref="AmbientSlot{T}"/> 纪律：只读快照、防双写（SEP.DeterministicCoreDisjoint）。
/// </summary>
public sealed record HangProbe(
    long Tick,                 // 心跳单调毫秒（ITimingClock.ElapsedMilliseconds）
    DateTimeOffset At,         // 心跳时刻（取证归档时间线）
    string? Phase,             // 慢路径当前阶段（如 ResolveInputs / Outbox flush / UI marshal）
    string? Resource,          // 等待资源（如 sqlite:ur_nodes / outbox-flush / ui-marshal:pageConversation）
    string? Owner)             // 声称持锁 / 进行中的写者标识
{
    /// <summary>生成一个以当前时钟刻度的心跳快照。</summary>
    public static HangProbe Beat(ITimingClock clock, string phase, string? resource = null, string? owner = null)
        => new(clock.ElapsedMilliseconds, clock.UtcNow, phase, resource, owner);
}

