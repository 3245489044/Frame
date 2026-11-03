using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 通用 task 生命周期端口实现：把状态跃迁落到通用任务池（写即激活 / 暂停门 / 终止）。
/// <para>宿主只提供「条目工厂」（载荷字段与初始化语义）；建账/激活/暂停/恢复/终止在此唯一实现。</para>
/// </summary>
public class TaskPoolLifecyclePort<TItem, TResult> : ITaskLifecyclePort
    where TItem : ITaskPoolItem
{
    private readonly TaskPool<TItem, TResult> _pool;
    private readonly Func<string, string?, string?, string?, TItem> _createItem;

    /// <param name="pool">通用任务池（唯一状态机）。</param>
    /// <param name="createItem">条目工厂：taskKey / input / subAgentTag / executorId → 宿主条目。</param>
    public TaskPoolLifecyclePort(
        TaskPool<TItem, TResult> pool,
        Func<string, string?, string?, string?, TItem> createItem)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _createItem = createItem ?? throw new ArgumentNullException(nameof(createItem));
    }

    public Task CreateTaskAsync(string taskKey, string? input = null, CancellationToken ct = default)
        => _pool.EnqueueAsync(_createItem(taskKey, input, null, null), ct);

    public Task CreateTaskAsync(string taskKey, string? input, string? subAgentTag, string? executorId,
        CancellationToken ct = default)
        => _pool.EnqueueAsync(_createItem(taskKey, input, subAgentTag, executorId), ct);

    public Task ActivateTaskAsync(string taskKey, CancellationToken ct = default)
        => _pool.SetActiveAsync(taskKey, true, ct);

    public Task PauseTaskAsync(string taskKey, CancellationToken ct = default)
        => _pool.PauseAsync(taskKey, ct: ct);

    public Task ResumeTaskAsync(string taskKey, CancellationToken ct = default)
        => _pool.ResumeAsync(taskKey, ct);

    public Task TerminateTaskAsync(string taskKey, CancellationToken ct = default)
        => _pool.CancelAsync(taskKey, ct);
}
