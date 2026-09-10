namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 常用状态转移便捷方法（基于 <see cref="IWorkerStateStore.Transition"/> 的带守卫组合）。
    /// </summary>
    public static class WorkerStateStoreExtensions
    {
        /// <summary>Starting → Running（spawn 完成、就绪）。</summary>
        public static bool StartWorker(this IWorkerStateStore store, string workerId)
            => store.Transition(workerId, PoolWorkerState.Starting, PoolWorkerState.Running);

        /// <summary>Running → Busy（请求开始）。</summary>
        public static bool MarkBusy(this IWorkerStateStore store, string workerId)
            => store.Transition(workerId, PoolWorkerState.Running, PoolWorkerState.Busy);

        /// <summary>Busy → Running（请求结束、回归空闲）。</summary>
        public static bool MarkFree(this IWorkerStateStore store, string workerId)
            => store.Transition(workerId, PoolWorkerState.Busy, PoolWorkerState.Running);

        /// <summary>Running|Busy → Hung（心跳/超时判挂起，可携带错误）。</summary>
        public static bool MarkHung(this IWorkerStateStore store, string workerId, string? error = null)
        {
            if (error != null) store.RecordError(workerId, error);
            return store.Transition(workerId, PoolWorkerState.Running, PoolWorkerState.Hung)
                || store.Transition(workerId, PoolWorkerState.Busy, PoolWorkerState.Hung);
        }

        /// <summary>任意非终态 → Stopped（杀进程/重启隔离）。</summary>
        public static bool StopWorker(this IWorkerStateStore store, string workerId)
            => store.Transition(workerId, PoolWorkerState.Starting, PoolWorkerState.Stopped)
                || store.Transition(workerId, PoolWorkerState.Running, PoolWorkerState.Stopped)
                || store.Transition(workerId, PoolWorkerState.Busy, PoolWorkerState.Stopped)
                || store.Transition(workerId, PoolWorkerState.Hung, PoolWorkerState.Stopped);
    }
}
