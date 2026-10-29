using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 通用任务池宿主门面：入队 / 入队并等待 / 暂停恢复 / 状态 / 启停。
/// <para>执行器解析、领域初始化与暂停门由宿主经构造函数注入。</para>
/// </summary>
public sealed class TaskPoolHost<TItem, TResult> : IAsyncDisposable
    where TItem : ITaskPoolItem
{
    private readonly TaskPool<TItem, TResult> _pool;
    private readonly TaskAwaiterRegistry<TResult> _awaiters = new();

    public TaskPoolHost(
        ITaskPoolPersistence<TItem> persistence,
        Func<TItem, string, CancellationToken, Task<TResult>> handler,
        TaskPoolOptions? options = null,
        ITaskPoolItemInitializer<TItem>? initializer = null,
        ITaskOutcomePolicy<TResult>? outcomePolicy = null)
    {
        _pool = new TaskPool<TItem, TResult>(
            persistence,
            async (item, executionId, ct) =>
            {
                try
                {
                    var result = await handler(item, executionId, ct).ConfigureAwait(false);
                    _awaiters.Complete(item.TaskKey, result);
                    return result;
                }
                catch (Exception ex)
                {
                    _awaiters.Fail(item.TaskKey, ex);
                    throw;
                }
            },
            options,
            initializer,
            outcomePolicy);
    }

    /// <summary>在途等待者数量（诊断）。</summary>
    public int PendingAwaiters => _awaiters.Count;

    public ITaskPool<TItem> Pool => _pool;

    public event EventHandler<TaskPoolStateChangedEventArgs>? TaskStateChanged
    {
        add => _pool.TaskStateChanged += value;
        remove => _pool.TaskStateChanged -= value;
    }

    public event EventHandler<TaskPoolConsumptionEventArgs>? ConsumptionObserved
    {
        add => _pool.ConsumptionObserved += value;
        remove => _pool.ConsumptionObserved -= value;
    }

    public void Start(CancellationToken ct = default) => _pool.Start(ct);

    public Task StopAsync() => _pool.StopAsync();

    public Task<long> EnqueueAsync(TItem item, CancellationToken ct = default) => _pool.EnqueueAsync(item, ct);

    public async Task<TResult> EnqueueAndWaitAsync(TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        // 同键并发等待 = 调用方误用：显式失败（不覆盖先到者 TCS，避免永久等待）。
        if (!_awaiters.TryRegister(item.TaskKey, out var completion))
            throw new InvalidOperationException(
                $"任务 {item.TaskKey} 已有一个等待中的 EnqueueAndWaitAsync；同一 taskKey 不支持并发等待。");

        try
        {
            await _pool.EnqueueAsync(item, ct).ConfigureAwait(false);
            return await completion!.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // 入队失败 / 取消 / 超时：释放等待位（任务仍可能在池内继续执行，此时不再回投结果）。
            _awaiters.Remove(item.TaskKey);
            throw;
        }
    }

    public Task PauseAsync(string taskKey, string? source = null, string? reason = null, CancellationToken ct = default)
        => _pool.PauseAsync(taskKey, source, reason, ct);

    public Task ResumeAsync(string taskKey, CancellationToken ct = default) => _pool.ResumeAsync(taskKey, ct);

    public Task<TaskExecutionStatus> GetStatusAsync(string taskKey, CancellationToken ct = default)
        => _pool.GetStatusAsync(taskKey, ct);

    public Task<int> ProcessPendingAsync(CancellationToken ct = default) => _pool.ProcessPendingAsync(ct);

    public ValueTask DisposeAsync() => _pool.DisposeAsync();
}
