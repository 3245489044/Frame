using Modules.Sync.Contracts;

namespace Modules.Sync.Replica;

/// <summary>
/// 客户端已应用快照的只读水位：只由下行 <see cref="Snapshot"/> 推进，不含任何本地预测字段、不含回滚快照。
/// <para>
/// 投影本体不在此处复制，副本只保留最后一条通过校验的 <see cref="SyncStateProjection"/> 引用；
/// 哈希只与同 tick 的权威哈希比对，绝不跨 tick 使用。
/// </para>
/// </summary>
public readonly struct AppliedState
{
    /// <summary>尚未应用任何快照：<see cref="LastAppliedTick"/> 为 -1，无时间基准，哈希为零值（未计算）。</summary>
    public static readonly AppliedState None = new(false, -1L, 0L, default);

    public AppliedState(bool hasApplied, long lastAppliedTick, long appliedCount, StateHash lastAppliedHash)
    {
        HasApplied = hasApplied;
        LastAppliedTick = hasApplied ? lastAppliedTick : -1L;
        AppliedCount = appliedCount;
        LastAppliedHash = hasApplied ? lastAppliedHash : default;
    }

    /// <summary>是否已应用过至少一条快照（决定客户端是否具备输入窗口的时间基准）。</summary>
    public bool HasApplied { get; }

    /// <summary>最后一条被接受快照的决策 tick；未应用时为 -1。</summary>
    public long LastAppliedTick { get; }

    /// <summary>已接受并应用的快照条数（不含丢弃）。</summary>
    public long AppliedCount { get; }

    /// <summary>最后一条被接受快照的权威状态哈希（同 tick 口径，可用于 <see cref="SnapshotAck"/>）。</summary>
    public StateHash LastAppliedHash { get; }
}
