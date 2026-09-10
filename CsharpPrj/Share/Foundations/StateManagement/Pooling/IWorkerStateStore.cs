using TConfig.ContextFrame.Abstractions;

namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 每 worker 一个存储会话（StorageCell）的状态存储：状态机 + 元数据。
    /// <see cref="Transition"/> 带 from 守卫（防非法/竞态转移），成功触发 <see cref="StateChanged"/>。
    /// 契约仅依赖 IStorageCell（TConfig），存储后端（InternalStorage）属实现细节（R2）。
    /// </summary>
    public interface IWorkerStateStore : IDisposable
    {
        /// <summary>登记 worker 并返回其隔离会话单元；已存在则返回现有单元（幂等）。</summary>
        IStorageCell GetOrCreate(string workerId);

        /// <summary>取 worker 会话单元（未登记返回 null）。</summary>
        IStorageCell? GetCell(string workerId);

        /// <summary>worker 是否已登记。</summary>
        bool Exists(string workerId);

        /// <summary>取 worker 状态快照（未登记抛 KeyNotFoundException）。</summary>
        WorkerStateInfo Get(string workerId);

        /// <summary>尝试取状态快照。</summary>
        bool TryGet(string workerId, out WorkerStateInfo? info);

        /// <summary>
        /// 带守卫的状态转移：当前状态必须等于 <paramref name="from"/> 才迁移到 <paramref name="to"/>；
        /// 否则返回 false（不改变状态、不触发事件）。
        /// </summary>
        bool Transition(string workerId, PoolWorkerState from, PoolWorkerState to);

        /// <summary>记录进程 pid（Config 层，不触发状态转移）。</summary>
        void SetPid(string workerId, int pid);

        /// <summary>更新最后活动时间（心跳推进）。</summary>
        void UpdateLastActivity(string workerId);

        /// <summary>记录错误（保留 lastError，不改变状态）。</summary>
        void RecordError(string workerId, string? error);

        /// <summary>超时计数 +1。</summary>
        void IncrementTimeoutCount(string workerId);

        /// <summary>移除 worker 并释放其会话。</summary>
        bool Remove(string workerId);

        /// <summary>全部已登记 worker id。</summary>
        IReadOnlyCollection<string> GetWorkerIds();

        /// <summary>状态变化事件（含初始 Starting 登记与每次元数据更新）。</summary>
        event Action<WorkerStateInfo>? StateChanged;
    }
}
