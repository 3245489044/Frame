namespace Modules.Sync.Replica;

/// <summary>
/// 客户端已应用快照的只读水位：只由下行快照推进，不含任何本地预测字段、不含回滚快照。
/// <para>
/// 依赖点（SY-00）：状态投影 <c>SyncStateProjection</c> 与 <c>StateHash</c> 由契约定义，
/// 本类型刻意不复制任何投影字段，避免在 Replica 侧长出第二套状态结构；
/// 副本要展示投影时，直接从契约消息里取，不在此处缓存。
/// </para>
/// </summary>
public readonly struct AppliedState
{
    /// <summary>尚未应用任何快照：<see cref="LastAppliedTick"/> 为 -1，无时间基准。</summary>
    public static readonly AppliedState None = new(false, -1L, 0L);

    public AppliedState(bool hasApplied, long lastAppliedTick, long appliedCount)
    {
        HasApplied = hasApplied;
        LastAppliedTick = hasApplied ? lastAppliedTick : -1L;
        AppliedCount = appliedCount;
    }

    /// <summary>是否已应用过至少一条快照（决定客户端是否具备输入窗口的时间基准）。</summary>
    public bool HasApplied { get; }

    /// <summary>最后一条被接受快照的决策 tick；未应用时为 -1。</summary>
    public long LastAppliedTick { get; }

    /// <summary>已接受并应用的快照条数（不含丢弃）。</summary>
    public long AppliedCount { get; }
}
