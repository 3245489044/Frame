namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// PoolInitializer —— 池启动编排：前置检查 → spawn N 个 worker → 心跳探活（可重试）。
    /// 只依赖 <see cref="IWorkerStateStore"/> 与注入的委托（<see cref="RegisterPrecondition"/> /
    /// <see cref="RegisterWorkerFactory"/> / <see cref="RegisterProbe"/>），传输无关、无外部存储编排器。
    /// 只做启动编排，不做运行期并发请求分发（那是池管理器 + 调度器的事）。
    /// </summary>
    public sealed class PoolInitializer : IPoolInitializer
    {
        private readonly IWorkerStateStore _stateStore;
        private Func<Task<bool>>? _precondition;
        private Func<WorkerStartSpec, CancellationToken, Task<WorkerHandle>>? _spawn;
        private Func<WorkerHandle, Task<bool>>? _probe;

        public int RetryLimit { get; set; } = 1;

        public PoolInitializer(IWorkerStateStore stateStore)
        {
            _stateStore = PortGuard.Required(stateStore, nameof(stateStore));
        }

        public void RegisterPrecondition(Func<Task<bool>> precondition)
            => _precondition = PortGuard.Required(precondition, nameof(precondition));

        public void RegisterWorkerFactory(Func<WorkerStartSpec, CancellationToken, Task<WorkerHandle>> spawn)
            => _spawn = PortGuard.Required(spawn, nameof(spawn));

        public void RegisterProbe(Func<WorkerHandle, Task<bool>> probe)
            => _probe = PortGuard.Required(probe, nameof(probe));

        public async Task<PoolInitResult> StartAsync(int count, CancellationToken ct = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

            var handles = new List<WorkerHandle>();

            try
            {
                // 1) 前置检查（可跳过）
                if (_precondition != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!await _precondition().ConfigureAwait(false))
                    {
                        return new PoolInitResult(
                            false, 0, Array.Empty<WorkerHandle>(),
                            "前置条件检查失败（如 cli.js/可执行文件不存在）。");
                    }
                }

                // 2) spawn N 个 worker
                if (_spawn == null)
                {
                    return new PoolInitResult(false, 0, Array.Empty<WorkerHandle>(), "未注册 worker 工厂（RegisterWorkerFactory）。");
                }

                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var spec = new WorkerStartSpec($"worker-{i}");
                    _stateStore.GetOrCreate(spec.WorkerId); // Starting
                    var handle = await _spawn(spec, ct).ConfigureAwait(false);
                    // 以框架权威的 spec.WorkerId 归一化，保证状态与会话一致
                    var normalized = handle with { WorkerId = spec.WorkerId };
                    _stateStore.SetPid(spec.WorkerId, normalized.Pid);
                    _stateStore.Transition(spec.WorkerId, PoolWorkerState.Starting, PoolWorkerState.Running);
                    handles.Add(normalized);
                }

                // 3) 心跳探活（可跳过；失败累计 + 补位判断）
                if (_probe != null)
                {
                    var unhealthy = new List<WorkerHandle>();
                    foreach (var handle in handles)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!await ProbeWithRetryAsync(handle, ct).ConfigureAwait(false))
                        {
                            _stateStore.MarkHung(handle.WorkerId, "心跳探活失败（已达重试上限）");
                            unhealthy.Add(handle);
                        }
                    }

                    if (unhealthy.Count > 0)
                    {
                        return new PoolInitResult(
                            false, CountStarted(handles), handles,
                            $"{unhealthy.Count} 个 worker 心跳探活失败: {string.Join(", ", unhealthy.Select(h => h.WorkerId))}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 解包到最深层消息，保留明确归因
                return new PoolInitResult(false, CountStarted(handles), handles, GetDeepestMessage(ex));
            }

            return new PoolInitResult(true, handles.Count, handles);
        }

        private async Task<bool> ProbeWithRetryAsync(WorkerHandle handle, CancellationToken ct)
        {
            for (var attempt = 0; attempt <= RetryLimit; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await _probe!(handle).ConfigureAwait(false))
                        return true;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* 探活抛异常按失败计，继续重试 */ }
            }
            return false;
        }

        private int CountStarted(List<WorkerHandle> handles)
            => handles.Count(h => _stateStore.TryGet(h.WorkerId, out var info) && info is { State: not PoolWorkerState.Hung });

        private static string GetDeepestMessage(Exception ex)
        {
            var current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }
            return current.Message;
        }
    }
}
