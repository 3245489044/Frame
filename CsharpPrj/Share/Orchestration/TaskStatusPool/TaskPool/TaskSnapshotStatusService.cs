using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务快照状态投影（通用）：把「状态/激活标志如何落到具体条目」从状态服务中解耦。
/// 缺省实现 <see cref="SnapshotTaskStatusProjection{TItem}"/> 按可选接口
/// （<see cref="IStatusAwareTaskPoolItem"/> / <see cref="IActivatableTaskPoolItem"/>）取用，未实现者维持无状态语义。
/// </summary>
public interface ITaskStatusProjection<TItem>
    where TItem : ITaskPoolItem
{
    TaskExecutionStatus GetStatus(TItem item);

    void SetStatus(TItem item, TaskExecutionStatus status);

    bool IsActive(TItem item);

    void SetActive(TItem item, bool active);
}

/// <summary>基于可选快照接口的缺省状态投影（未实现终态/激活接口的条目按「无门控」处理）。</summary>
public sealed class SnapshotTaskStatusProjection<TItem> : ITaskStatusProjection<TItem>
    where TItem : ITaskPoolItem
{
    public static readonly SnapshotTaskStatusProjection<TItem> Instance = new();

    public TaskExecutionStatus GetStatus(TItem item)
        => item is IStatusAwareTaskPoolItem statusAware ? statusAware.Status : TaskExecutionStatus.NotStarted;

    public void SetStatus(TItem item, TaskExecutionStatus status)
    {
        if (item is IStatusAwareTaskPoolItem statusAware) statusAware.Status = status;
    }

    public bool IsActive(TItem item)
        => item is not IActivatableTaskPoolItem activatable || activatable.Active;

    public void SetActive(TItem item, bool active)
    {
        if (item is IActivatableTaskPoolItem activatable) activatable.Active = active;
    }
}

/// <summary>
/// 任务快照状态服务（通用，Agent 无关）：把「直写状态（绕过消费循环）」收敛为单一实现 ——
/// 读取持久化快照 → 置状态 → 终态收敛 <c>Active=false</c> → 写回载荷；并提供
/// 「内存未命中回退持久化」的状态读取与「是否可出队候选」诊断探针。
/// <para>语义纪律：<c>Completed / Faulted / Cancelled</c> 为终态，终态不得再带 <c>Active=true</c>
/// （否则重启回填会把已终结任务重新当作可消费候选）。</para>
/// </summary>
public sealed class TaskSnapshotStatusService<TItem>
    where TItem : ITaskPoolItem
{
    private readonly ITaskPoolPersistence<TItem> _persistence;
    private readonly ITaskStatusProjection<TItem> _projection;

    public TaskSnapshotStatusService(
        ITaskPoolPersistence<TItem> persistence,
        ITaskStatusProjection<TItem>? projection = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _projection = projection ?? SnapshotTaskStatusProjection<TItem>.Instance;
    }

    /// <summary>终态判定（唯一集合：Completed / Faulted / Cancelled）。</summary>
    public static bool IsTerminal(TaskExecutionStatus status)
        => status is TaskExecutionStatus.Completed
            or TaskExecutionStatus.Faulted
            or TaskExecutionStatus.Cancelled;

    /// <summary>
    /// 直写快照状态（不进入消费循环、不激活）：快照不存在返回 null（不伪造新行）。
    /// <paramref name="mutate"/> 供调用方在同一写入内附带字段（如 Failure 信封）。
    /// </summary>
    public async Task<TItem?> ApplyAsync(
        string taskKey,
        TaskExecutionStatus status,
        Action<TItem>? mutate = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return default;
        var key = taskKey.Trim();
        var item = await _persistence.GetAsync(key, ct).ConfigureAwait(false);
        if (item is null) return default;

        mutate?.Invoke(item);
        _projection.SetStatus(item, status);
        if (IsTerminal(status)) _projection.SetActive(item, false);
        await _persistence.UpdatePayloadAsync(key, item, ct).ConfigureAwait(false);
        return item;
    }

    /// <summary>读取持久化权威状态（不存在 ⇒ NotStarted）。</summary>
    public async Task<TaskExecutionStatus> ReadAsync(string taskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return TaskExecutionStatus.NotStarted;
        var item = await _persistence.GetAsync(taskKey.Trim(), ct).ConfigureAwait(false);
        return item is null ? TaskExecutionStatus.NotStarted : _projection.GetStatus(item);
    }

    /// <summary>
    /// 可出队候选探针（诊断）：持久化 <see cref="TaskExecutionStatus.NotStarted"/> 且激活为真。
    /// 读侧状态已对齐但任务长期不被消费时，本探针返回 false 即提示落库未置 Active（幽灵/直写遗留行）。
    /// </summary>
    public async Task<bool> IsDequeuableCandidateAsync(string taskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return false;
        var item = await _persistence.GetAsync(taskKey.Trim(), ct).ConfigureAwait(false);
        if (item is null) return false;
        return _projection.GetStatus(item) == TaskExecutionStatus.NotStarted && _projection.IsActive(item);
    }
}
