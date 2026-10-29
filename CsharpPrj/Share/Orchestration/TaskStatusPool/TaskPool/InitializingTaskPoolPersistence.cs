using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 初始化装饰器（通用）：把 <see cref="ITaskPoolItemInitializer{TItem}"/> 的调用点从
/// 「仅任务池 <c>EnqueueAsync</c>」扩展到**所有经持久化端口的写入路径**
/// （直写簿记 / 会话存储端口 / 生命周期端口 / 休眠重载），消除「绕过池入队即绕过统一初始化」的缺口。
/// <para>幂等前提：初始化钩子必须可重复调用（播种默认值 / 镜像视图均为幂等写入）。
/// 与池内 initializer 同时启用会导致重复调用，故装配时应二选一（建议：装饰器为唯一调用点）。</para>
/// </summary>
public sealed class InitializingTaskPoolPersistence<TItem> : ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    private readonly ITaskPoolPersistence<TItem> _inner;
    private readonly ITaskPoolItemInitializer<TItem> _initializer;

    public InitializingTaskPoolPersistence(
        ITaskPoolPersistence<TItem> inner,
        ITaskPoolItemInitializer<TItem> initializer)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
    }

    /// <summary>被装饰的持久化端口（诊断 / 高级调用）。</summary>
    public ITaskPoolPersistence<TItem> Inner => _inner;

    /// <summary>幂等包装：已是同类装饰器直接返回，避免多层重复初始化。</summary>
    public static ITaskPoolPersistence<TItem> Ensure(
        ITaskPoolPersistence<TItem> persistence,
        ITaskPoolItemInitializer<TItem> initializer)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(initializer);
        return persistence is InitializingTaskPoolPersistence<TItem> ? persistence
            : new InitializingTaskPoolPersistence<TItem>(persistence, initializer);
    }

    public Task InitializeAsync(CancellationToken ct = default)
        => _inner.InitializeAsync(ct);

    public Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
    {
        if (item is not null) _initializer.Initialize(item);
        return _inner.EnqueueAsync(item!, ct);
    }

    public Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default)
        => _inner.ListAsync(status, limit, ct);

    public Task<TItem?> GetAsync(string taskKey, CancellationToken ct = default)
        => _inner.GetAsync(taskKey, ct);

    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
        => _inner.UpdateStatusAsync(taskKey, from, to, ct);

    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
        => _inner.UpdatePayloadAsync(taskKey, item, ct);

    public Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default)
        => _inner.DeleteAsync(taskKey, ct);

    public Task<PagedTaskPool<TItem>> GetPageAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
        => _inner.GetPageAsync(offset, pageSize, status, ct);

    public Task<IReadOnlyList<string>> ListKeysAsync(int limit, CancellationToken ct = default)
        => _inner.ListKeysAsync(limit, ct);

    public Task<IReadOnlyList<TItem>> GetManyAsync(IReadOnlyCollection<string> taskKeys, CancellationToken ct = default)
        => _inner.GetManyAsync(taskKeys, ct);
}
