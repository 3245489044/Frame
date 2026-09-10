namespace Share.StateManagement.Pooling
{
    /// <summary>池健康/统计快照（按 <see cref="PoolWorkerState"/> 计数）。</summary>
    public sealed record PoolHealthSnapshot(
        int Total,
        int Starting,
        int Running,
        int Busy,
        int Hung,
        int Stopped,
        int EnsureReadyFastPathHits)
    {
        /// <summary>宽松健康判定：无挂起且尚有存活的非 Stopped worker。</summary>
        public bool IsHealthy => Hung == 0 && (Total - Stopped) > 0;
    }
}
