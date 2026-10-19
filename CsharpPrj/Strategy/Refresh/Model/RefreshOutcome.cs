namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 一次刷新的产物结论（L0 契约，零领域）：
    /// <list type="bullet">
    /// <item><see cref="Changed"/> —— 内容变化，应交付。</item>
    /// <item><see cref="Unchanged"/> —— 内容未变，合法 no-op（不触写落地）。</item>
    /// <item><see cref="Skipped"/> —— 条件不满足 / 版本过期，不交付。</item>
    /// <item><see cref="Failed"/> —— 渲染失败（带错误原因）。</item>
    /// <item><see cref="Blocked"/> —— 依赖未就绪，当前不可刷新。</item>
    /// </list>
    /// </summary>
    public enum RefreshOutcome
    {
        Changed,
        Unchanged,
        Skipped,
        Failed,
        Blocked,
    }
}
