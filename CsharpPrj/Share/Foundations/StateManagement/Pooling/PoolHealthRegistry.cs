namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// PoolHealthRegistry —— 池健康/统计聚合（基于 <see cref="IWorkerStateStore"/> 各状态计数）。
    /// 订阅 StateChanged，任意 worker 状态变化时触发 <see cref="IPoolHealth.Changed"/>。
    /// </summary>
    public sealed class PoolHealthRegistry : IPoolHealth
    {
        private readonly IWorkerStateStore _stateStore;

        public event Action<PoolHealthSnapshot>? Changed;

        public PoolHealthRegistry(IWorkerStateStore stateStore)
        {
            _stateStore = PortGuard.Required(stateStore, nameof(stateStore));
            _stateStore.StateChanged += OnWorkerStateChanged;
        }

        public PoolHealthSnapshot GetSnapshot()
        {
            int starting = 0, running = 0, busy = 0, hung = 0, stopped = 0;

            foreach (var id in _stateStore.GetWorkerIds())
            {
                if (!_stateStore.TryGet(id, out var info) || info is null)
                    continue;

                switch (info.State)
                {
                    case PoolWorkerState.Starting: starting++; break;
                    case PoolWorkerState.Running: running++; break;
                    case PoolWorkerState.Busy: busy++; break;
                    case PoolWorkerState.Hung: hung++; break;
                    case PoolWorkerState.Stopped: stopped++; break;
                }
            }

            var total = starting + running + busy + hung + stopped;
            // 旧 InternalStorage 路径无 EnsureReadyAsync 快速路径语义，命中计数恒 0。
            return new PoolHealthSnapshot(total, starting, running, busy, hung, stopped, 0);
        }

        private void OnWorkerStateChanged(WorkerStateInfo info) => Changed?.Invoke(GetSnapshot());
    }
}
