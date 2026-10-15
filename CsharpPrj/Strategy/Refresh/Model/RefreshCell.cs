namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 刷新单元（L1，登记用，零领域）：一个可独立刷新的槽（SlotKey）与其渲染产物（object，框架不解释类型）。
    /// </summary>
    public sealed record RefreshCell(string SlotKey, object Payload);
}
