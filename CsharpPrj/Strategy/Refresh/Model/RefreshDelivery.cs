namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 交付信封（L0 契约）：写落地端口收到的完整一次刷新交付
    /// { SlotKey, Payload(object), Version, Fingerprint, TraceId }。
    /// </summary>
    public sealed record RefreshDelivery(
        string SlotKey,
        object Payload,
        long Version,
        string Fingerprint,
        string? TraceId);
}

