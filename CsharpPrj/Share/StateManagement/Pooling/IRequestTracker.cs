namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 请求↔worker 关联追踪（沿用 EcsSessionManager 会话追踪模式）。
    /// 运行期分发/并发闸门属传输层（ClineClient + AEF）职责，本契约仅记录关联，不执行分发。
    /// </summary>
    public interface IRequestTracker : IDisposable
    {
        /// <summary>为指定 worker 分配一个新请求 id，并建立关联。</summary>
        string Assign(string workerId);

        /// <summary>取请求所属 worker（未分配返回 null）。</summary>
        string? GetWorker(string requestId);

        /// <summary>结束请求，解除关联。返回是否成功移除。</summary>
        bool Complete(string requestId);

        /// <summary>当前活跃请求数。</summary>
        int ActiveRequestCount { get; }

        /// <summary>全部活跃请求 id。</summary>
        IReadOnlyCollection<string> GetActiveRequestIds();

        /// <summary>指定 worker 上活跃的请求 id 集合。</summary>
        IReadOnlyCollection<string> GetActiveRequestsForWorker(string workerId);
    }
}
