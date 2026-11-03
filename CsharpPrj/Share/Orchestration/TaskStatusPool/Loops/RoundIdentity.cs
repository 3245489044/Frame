namespace Share.TaskStatusPool.Loops
{
    /// <summary>
    /// 轮号唯一口径（E2 / DEC-2 = 全面 1 基）：落账 / 日志 / ScopeId / 观测三处同源，禁各自 +1 或 max(1) 归一。
    /// <para>租约 <c>Round</c> 为 0 基执行游标；对外可见轮号一律经本类型派生（1 基）。</para>
    /// </summary>
    public readonly record struct RoundIdentity(int Number)
    {
        public static RoundIdentity Of(int leaseRound) => new(leaseRound + 1);
    }
}
