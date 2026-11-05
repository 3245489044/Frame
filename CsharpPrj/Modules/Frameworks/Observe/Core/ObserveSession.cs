using System;

namespace Modules.Observe;

/// <summary>
/// 一次 <see cref="ChangeCommand{T}"/> 的收敛会话状态机：Created → Accepted → Applied → Converged，异常路径 Timeout/Degraded。
/// 持有 id/key/目标版本/最高 ack 级别/窗口起止/结果；会话在 Converged 或 Degraded 后终结回收。
/// </summary>
public sealed class ObserveSession
{
    public ObserveSession(string changeId, string key, long version, BarrierMode barrier, TimeSpan timeout)
    {
        ChangeId = changeId;
        Key = key;
        Version = version;
        Barrier = barrier;
        Timeout = timeout;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string ChangeId { get; }
    public string Key { get; }
    public long Version { get; }
    public BarrierMode Barrier { get; }
    public TimeSpan Timeout { get; }
    public DateTimeOffset StartedAt { get; }

    public AckLevel HighestAck { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public DateTimeOffset? ConvergedAt { get; private set; }

    /// <summary>应用一条 ack：升级最高 ack 级别并记录里程碑时刻（ack 只升不降）。</summary>
    public void Upgrade(AckLevel level, DateTimeOffset at)
    {
        if (level > HighestAck)
            HighestAck = level;
        if (level == AckLevel.Applied)
            AppliedAt ??= at;
        if (level == AckLevel.Converged)
            ConvergedAt = at;
    }
}
