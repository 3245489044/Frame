namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 写入接受决策（L0 契约）。
    /// <list type="bullet">
    /// <item><see cref="Apply"/> —— 可落地（经 sink 一次）。</item>
    /// <item><see cref="Skip"/> —— 指纹未变（内容与上次相同），不触 DOM。</item>
    /// <item><see cref="Stale"/> —— 版本过期（version &lt; baseline），不触 DOM。</item>
    /// </list>
    /// </summary>
    public enum AcceptDecision
    {
        Apply,
        Skip,
        Stale,
    }
}

