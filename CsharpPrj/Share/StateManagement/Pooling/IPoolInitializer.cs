namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 池启动编排（包装 InitializationOrchestrator）：前置检查 → spawn N 个 worker → 心跳探活（可重试）。
    /// 进程/IPC/超时经委托注入（<see cref="RegisterWorkerFactory"/> / <see cref="RegisterProbe"/>），本契约不含传输细节。
    /// 仅做启动编排，不做运行期并发请求分发（那是池管理器 + 调度器的事）。
    /// </summary>
    public interface IPoolInitializer
    {
        /// <summary>注册启动前置检查（如 cli.js/可执行文件存在）；未注册则跳过。</summary>
        void RegisterPrecondition(Func<Task<bool>> precondition);

        /// <summary>注册 worker 工厂（spawn 单个 worker），必须注册。</summary>
        void RegisterWorkerFactory(Func<WorkerStartSpec, CancellationToken, Task<WorkerHandle>> spawn);

        /// <summary>注册心跳探活；未注册则跳过健康检查。</summary>
        void RegisterProbe(Func<WorkerHandle, Task<bool>> probe);

        /// <summary>健康检查重试次数（首探之外的重试上限，默认 1）。</summary>
        int RetryLimit { get; set; }

        /// <summary>启动 count 个 worker 并完成健康检查。</summary>
        Task<PoolInitResult> StartAsync(int count, CancellationToken ct = default);
    }
}
