using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResult>> _awaiters = new(StringComparer.Ordinal);

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
                    Complete(item.TaskKey, result);
                    return result;
                }
                catch (Exception ex)
                {
                    Fail(item.TaskKey, ex);
                    throw;
                }
            },
            options,
            initializer,
            outcomePolicy);
    }

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
        var tcs = _awaiters.GetOrAdd(item.TaskKey, static _ => new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously));
        await _pool.EnqueueAsync(item, ct).ConfigureAwait(false);
        using var registration = ct.Register(static state => ((TaskCompletionSource<TResult>)state!).TrySetCanceled(), tcs);
        return await tcs.Task.ConfigureAwait(false);
    }

    public Task PauseAsync(string taskKey, string? source = null, string? reason = null, CancellationToken ct = default)
        => _pool.PauseAsync(taskKey, source, reason, ct);

    public Task ResumeAsync(string taskKey, CancellationToken ct = default) => _pool.ResumeAsync(taskKey, ct);

    public Task<TaskExecutionStatus> GetStatusAsync(string taskKey, CancellationToken ct = default)
        => _pool.GetStatusAsync(taskKey, ct);

    public Task<int> ProcessPendingAsync(CancellationToken ct = default) => _pool.ProcessPendingAsync(ct);

    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    private void Complete(string taskKey, TResult result)
    {
        if (_awaiters.TryRemove(taskKey, out var tcs)) tcs.TrySetResult(result);
    }

    private void Fail(string taskKey, Exception ex)
    {
        if (_awaiters.TryRemove(taskKey, out var tcs)) tcs.TrySetException(ex);
    }
}
