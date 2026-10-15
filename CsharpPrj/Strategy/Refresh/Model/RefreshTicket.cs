using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 单调版本凭证（L0 契约）：一次写作请求携带的 slot 目标 + 版本 + 追踪号。
    /// <para><see cref="Version"/> 由单一状态源 <see cref="IRefreshStore.NextVersion"/> 单调分配；
    /// <see cref="TraceId"/> 可为 null（如页面兜底轮询无 trace），但版本必须单调，否则无法判过期。</para>
    /// </summary>
    public readonly record struct RefreshTicket(string SlotKey, long Version, string? TraceId);
}

