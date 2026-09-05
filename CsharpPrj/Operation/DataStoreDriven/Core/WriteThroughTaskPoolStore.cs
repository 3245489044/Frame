using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.TaskStatusPool.TaskPool;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 任务池写透守卫（对齐公理 DerivedDataAutoMaintenance，见 Agent/Axioms）：包装 <see cref="ITaskPoolPersistence{TItem}"/>，
/// 入队（<see cref="EnqueueAsync"/>）自动建 task 图资源镜像、删除（<see cref="DeleteAsync"/>）自动级联移除镜像。
/// 任务只建一次图资源镜像（幂等）；状态/载荷变化（UpdateStatus/UpdatePayload）**不**重建镜像，避免任务高频变化导致的图抖动（对齐计划 §六）。
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

    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
        => UpdateAndMirrorAsync(taskKey, () => _inner.UpdateStatusAsync(taskKey, from, to, ct), ct);

    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
        => UpdateAndMirrorAsync(taskKey, () => _inner.UpdatePayloadAsync(taskKey, item, ct), ct);

    /// <summary>先落 SQL，再经写透守卫同步 LiteDB 引用图端点（幂等注册，不重建边，避免图抖动；满足「每次 SQL 更新都同步 LiteDB」）。
    /// UpdateStatus/UpdatePayload 统一经此入口，消除重复镜像调用（RedundancyElimination）。ct 透传镜像（B4），调用方取消即中止镜像。</summary>
    private async Task<T> UpdateAndMirrorAsync<T>(string taskKey, Func<Task<T>> sqlUpdate, CancellationToken ct)
    {
        var result = await sqlUpdate().ConfigureAwait(false);
        await _grounding.EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>非泛型重载委托泛型版（D3 收拢）：仅因 <see cref="UpdatePayloadAsync"/> 返回 <see cref="Task"/>（非 <c>Task&lt;T&gt;</c>）而并存。</summary>
    private Task UpdateAndMirrorAsync(string taskKey, Func<Task> sqlUpdate, CancellationToken ct)
        => UpdateAndMirrorAsync(taskKey, async () => { await sqlUpdate().ConfigureAwait(false); return true; }, ct);

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
