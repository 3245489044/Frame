namespace Share.StateManagement
{
    /// <summary>
    /// 不可变状态快照 + 版本。版本单调递增，投影器按版本去重（治 L4 竞态）。
    /// </summary>
    public sealed record StateSnapshot<TState>(
        TState Value,
        long Version,
        DateTime UpdatedAtUtc,
        string? Source);
}
