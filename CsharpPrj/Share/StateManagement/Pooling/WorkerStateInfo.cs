namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 单 worker 状态快照（含 daemon 元数据：pid、启动时间、busy/free、lastError、timeoutCount）。
    /// <see cref="IsBusy"/> 为状态派生（State==Busy）。
    /// </summary>
    public sealed record WorkerStateInfo(
        string WorkerId,
        PoolWorkerState State,
        int Pid,
        DateTime StartedAtUtc,
        DateTime LastActivityUtc,
        string? LastError,
        int TimeoutCount)
    {
        /// <summary>busy 状态派生（State==Busy），非独立存储字段，避免派生值手工同步失配。</summary>
        public bool IsBusy => State == PoolWorkerState.Busy;

        /// <summary>新 worker 初始快照（Starting）。</summary>
        public static WorkerStateInfo Initial(string workerId)
        {
            var now = DateTime.UtcNow;
            return new WorkerStateInfo(workerId, PoolWorkerState.Starting, 0, now, now, null, 0);
        }
    }
}
