
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 内存持久化占位实现（测试 / 无后端引导 / 占位）。线程安全；进程内即可验证任务池状态机语义。
/// </summary>
public sealed class InMemoryTaskPoolPersistence<TItem> : ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    private readonly ConcurrentDictionary<string, TaskPoolEntry<TItem>> _items = new(StringComparer.Ordinal);
    private long _seq;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        // 快照驱动（IStatusAwareTaskPoolItem）：Status 取自载荷；否则状态机初始 NotStarted。
        var status = item is IStatusAwareTaskPoolItem sa ? sa.Status : TaskExecutionStatus.NotStarted;
        _items[item.TaskKey] = new TaskPoolEntry<TItem>(item, status, 0);
        return Task.FromResult(Interlocked.Increment(ref _seq));
    }

    public Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default)
    {
        var list = _items.Values
            .Where(e => e.Status == status)
            .OrderByDescending(e => e.Item.Priority)
            .Select(e => e.Item)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<TItem>>(list);
    }

    public Task<TItem?> GetAsync(string taskKey, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(taskKey, out var entry) ? entry.Item : default);

    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
    {
        if (!_items.TryGetValue(taskKey, out var entry) || entry.Status != from)
            return Task.FromResult(false);
        _items[taskKey] = entry with { Status = to };
        return Task.FromResult(true);
    }

    /// <summary>更新载荷 PayloadJson（运行时快照字段）。快照驱动条目（<see cref="IStatusAwareTaskPoolItem"/>）同时同步条目状态。</summary>
    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.TryGetValue(taskKey, out var entry))
        {
            // 快照驱动：条目 Status 与载荷内 Status 同步（消除双写漂移）；否则仅替换载荷。
            _items[taskKey] = item is IStatusAwareTaskPoolItem sa
                ? entry with { Item = item, Status = sa.Status }
                : entry with { Item = item };
        }
        return Task.CompletedTask;
    }

    public Task<PagedTaskPool<TItem>> GetPageAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var query = _items.Values.AsEnumerable();
        if (status is not null) query = query.Where(e => e.Status == status);
        var ordered = query.OrderByDescending(e => e.Item.Priority).ThenBy(e => e.Item.TaskKey).ToList();
        var total = ordered.Count;
        var page = ordered.Skip(Math.Max(0, offset)).Take(Math.Max(1, pageSize)).Select(e => e.Item).ToList();
        return Task.FromResult(new PagedTaskPool<TItem>(page, total));
    }

    /// <summary>删除一条任务（持久化清理）。返回是否命中删除（不存在返回 false）。</summary>
    public Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryRemove(taskKey, out _));
    }
}
