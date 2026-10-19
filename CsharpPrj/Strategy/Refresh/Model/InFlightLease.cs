using System;

namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 在途租约（L0 契约，零领域）：用「身份 + 年龄 + 心跳 + 截止 + 进度令牌」表达在途，
    /// 取代布尔型在途判定。
    /// <para><see cref="Key"/> 是刷新槽 / 范围键（不是领域任务键）；同槽多租约由域适配器聚合或逐条给出。</para>
    /// <para><see cref="Kind"/> 为不透明词表：框架只消费 <c>Executable</c>；<c>Signal</c> / <c>Bookkeeping</c> 永不参与心跳。</para>
    /// <para><see cref="Active"/> = false 的租约永不参与心跳（休眠簿记即使实现了接口也不得唤醒刷新）。</para>
    /// </summary>
    public sealed record InFlightLease(
        string Key,
        string Kind,
        string LeaseId,
        DateTimeOffset StartedAt,
        DateTimeOffset LastHeartbeatAt,
        DateTimeOffset? Deadline,
        string? ProgressToken,
        bool Active = true)
    {
        /// <summary>框架唯一消费的可执行租约词（大小写不敏感比较）。</summary>
        public const string ExecutableKind = "Executable";
    }
}
