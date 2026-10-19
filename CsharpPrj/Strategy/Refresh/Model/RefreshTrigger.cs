namespace Strategy.Refresh.Model
{
    /// <summary>刷新触发车道（L0 契约）：一次刷新由谁驱动。</summary>
    public enum RefreshTrigger
    {
        /// <summary>事件驱动（变化源 / MarkDirty）。</summary>
        Change,

        /// <summary>在途租约心跳。</summary>
        Heartbeat,

        /// <summary>无事件 / 无租约时的兜底轮询。</summary>
        Fallback,

        /// <summary>用户显式刷新。</summary>
        Manual,

        /// <summary>页面回投刷新。</summary>
        PagePull,
    }
}
