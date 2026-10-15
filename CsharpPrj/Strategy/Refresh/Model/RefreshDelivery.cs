namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 交付信封（L0 契约）：写落地端口收到的完整一次刷新交付
    /// { SlotKey, Payload(object), Version, Fingerprint, TraceId }。
    /// <para><b>【预留】</b>预定消费方：<c>Core/RefreshArbiter</c> → <c>IRefreshSink.DeliverAsync</c>
    /// （待写落地端口接入后激活）。</para>
    /// </summary>
    public sealed record RefreshDelivery(
        string SlotKey,
        object Payload,
        long Version,
        string Fingerprint,
        string? TraceId);
}

