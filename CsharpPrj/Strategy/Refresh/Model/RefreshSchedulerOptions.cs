using System;

namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 调度器运行参数（L0 契约，零领域）：节拍 / 心跳上限 / 熔断阈值 / 租约上限 / 时钟。
    /// <para>所有阈值集中于此，并由 <see cref="RefreshContract"/> 提供默认值，禁止域侧自建节拍常量。</para>
    /// </summary>
    public sealed record RefreshSchedulerOptions
    {
        public int TickMs { get; init; } = RefreshContract.TickMs;

        public int MaxHeartbeatMs { get; init; } = RefreshContract.MaxHeartbeatMs;

        public int StallAfterNoChange { get; init; } = RefreshContract.StallAfterNoChange;

        public int StallWindowMs { get; init; } = RefreshContract.StallWindowMs;

        public TimeSpan LeaseMaxAge { get; init; } = TimeSpan.FromMinutes(30);

        /// <summary>注入时钟（测试可替换；缺省 UTC 墙钟）。</summary>
        public Func<DateTimeOffset> UtcNow { get; init; } = static () => DateTimeOffset.UtcNow;
    }
}
