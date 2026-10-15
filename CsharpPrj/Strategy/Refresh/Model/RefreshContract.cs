namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 刷新契约常量（L0，**全库唯一声明处**）：宿主 C# 与页面 JS（经模板占位注入）两端同源，
    /// 消除双写漂移（改一处两端同步生效）。
    /// </summary>
    public static class RefreshContract
    {
        /// <summary>回投刷新命令词（对齐页面 <c>data-poll-cmd</c> / 桥载荷 <c>cmd</c>）。</summary>
        public const string Command = "refresh";

        /// <summary>刷新节拍（ms）：dirty 合并窗口 + 在途心跳周期（宿主调度器 TickMs）。</summary>
        public const int TickMs = 1000;

        /// <summary>回投节流窗口（ms）：窗口内多次变更合并为一次回投（对齐 <c>data-refresh-throttle</c>）。</summary>
        public const int ThrottleMs = 1000;

        /// <summary>兜底轮询周期（ms）：页面 / 宿主在无推送时的兜底刷新周期（对齐 <c>data-poll-interval</c>）。</summary>
        public const int FallbackPollMs = 30000;
    }
}
