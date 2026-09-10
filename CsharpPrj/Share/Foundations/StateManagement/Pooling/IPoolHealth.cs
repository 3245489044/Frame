namespace Share.StateManagement.Pooling
{
    /// <summary>池健康/统计聚合（基于 <see cref="IWorkerStateStore"/> 各状态计数）。</summary>
    public interface IPoolHealth
    {
        /// <summary>当前池健康快照。</summary>
        PoolHealthSnapshot GetSnapshot();

        /// <summary>任意 worker 状态变化时触发（携带最新快照）。</summary>
        event Action<PoolHealthSnapshot>? Changed;
    }
}
