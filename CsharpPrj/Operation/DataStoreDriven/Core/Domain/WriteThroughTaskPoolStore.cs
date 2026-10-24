using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.TaskStatusPool.TaskPool;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 任务池写透守卫（对齐公理 DerivedDataAutoMaintenance，见 Agent/Axioms）：包装 <see cref="ITaskPoolPersistence{TItem}"/>，
/// 入队（<see cref="EnqueueAsync"/>）自动建 task 图资源镜像、删除（<see cref="DeleteAsync"/>）自动级联移除镜像。
/// 任务只建一次图资源镜像（幂等）；状态/载荷更新**不**触达图库（端点已在入队建好，避免任务高频变化导致图抖动/写放大；
/// 端点缺失由一致性守护的对账补齐，见 IStorageConsistencyGuard）。
/// </summary>
public sealed class WriteThroughTaskPoolStore<TItem> : ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    private readonly ITaskPoolPersistence<TItem> _inner;
    private readonly ReferenceGrounding _grounding;

    public WriteThroughTaskPoolStore(ITaskPoolPersistence<TItem> inner, ReferenceGrounding grounding)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
    }

    public Task InitializeAsync(CancellationToken ct = default) => _inner.InitializeAsync(ct);

    /// <summary>入队 → 写透 task 图镜像（Id=taskKey，幂等）。</summary>
    public async Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var id = await _inner.EnqueueAsync(item, ct).ConfigureAwait(false);
        await _grounding.EnsureTaskMirrorAsync(item.TaskKey, ct).ConfigureAwait(false);
        return id;
    }

    public Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default)
        => _inner.ListAsync(status, limit, ct);

    public Task<TItem?> GetAsync(string taskKey, CancellationToken ct = default) => _inner.GetAsync(taskKey, ct);

    /// <summary>状态更新：只写 SQL（不建/不重建图端点；漂移由对账补）。</summary>
    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
        => _inner.UpdateStatusAsync(taskKey, from, to, ct);

    /// <summary>载荷更新：只写 SQL（轮边界快照图写次数 = 0）。</summary>
    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
        => _inner.UpdatePayloadAsync(taskKey, item, ct);

    /// <summary>删除任务 → 级联移除 task 图镜像（RemoveResourceAsync 级联清边）。</summary>
    public async Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default)
    {
        var removed = await _inner.DeleteAsync(taskKey, ct).ConfigureAwait(false);
        if (removed)
            await _grounding.RemoveTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
        return removed;
    }

    public Task<PagedTaskPool<TItem>> GetPageAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
        => _inner.GetPageAsync(offset, pageSize, status, ct);
}
