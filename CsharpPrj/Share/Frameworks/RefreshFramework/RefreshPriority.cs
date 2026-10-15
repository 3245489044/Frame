namespace Share.RefreshFramework
{
    /// <summary>
    /// 刷新优先级（L1，通用）：
    /// <list type="bullet">
    /// <item><see cref="High"/> —— 事件驱动变更（宿主/UI 显式上报），须尽快推送。</item>
    /// <item><see cref="Low"/> —— 兜底轮询等低优先级生产者（页面兜底轮询收编为调度器管辖），**不得抢占 High**。</item>
    /// </list>
    /// </summary>
    public enum RefreshPriority
    {
        High,
        Low,
    }
}
