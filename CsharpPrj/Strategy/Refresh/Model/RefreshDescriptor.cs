namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 刷新源描述（L1，登记用，零领域）：申报一个变化源的身份 / 名称 / 可选节拍 / 订阅键 / 优先级。
    /// <para>领域词只出现在此描述里（由上层注入），框架不解释其语义。</para>
    /// </summary>
    /// <param name="SourceId">源唯一标识（进程内唯一；重复登记按键幂等 / 换实例替换）。</param>
    /// <param name="Name">源显示名（诊断用）。</param>
    /// <param name="TickMs">该源的独立节拍（null = 用框架默认 <see cref="RefreshContract.TickMs"/>）。</param>
    /// <param name="SubscribeKey">订阅键（域自定义；如页面 <c>data-poll-subscribe</c>）。</param>
    /// <param name="Priority">源优先级（兜底轮询类须置 <see cref="RefreshPriority.Low"/>）。</param>
    public sealed record RefreshDescriptor(
        string SourceId,
        string Name,
        int? TickMs = null,
        string? SubscribeKey = null,
        RefreshPriority Priority = RefreshPriority.High);
}
