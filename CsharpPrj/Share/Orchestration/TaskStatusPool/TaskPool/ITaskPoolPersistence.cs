
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务池持久化端口：通用任务池只依赖本抽象，不耦合具体后端（Sqlite / LiteDB / Local / InMemory）。
/// 状态字段（Status/RetryCount）为持久化镜像；运行时以状态机为准，落盘供重启回填。
/// </summary>
public interface ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>写入一条任务（初始状态 NotStarted）。返回持久化序号（append-only 主键语义）。</summary>
    Task<long> EnqueueAsync(TItem item, CancellationToken ct = default);

    /// <summary>按状态列出任务（调用方自行按 Priority 排序取用）。</summary>
    Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default);

    Task<TItem?> GetAsync(string taskKey, CancellationToken ct = default);

    /// <summary>条件状态迁移持久化（from→to；不匹配则返回 false，供失败时静默跳过）。</summary>
    Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default);

    /// <summary>仅更新载荷 PayloadJson（含运行时快照字段），不改变状态列。供运行中每轮边界持久化运行时快照。
    /// 状态列（Status）仍由 <see cref="UpdateStatusAsync"/> 在每次状态迁移时同步；PayloadJson 为权威状态基座。</summary>
    Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default);

    /// <summary>删除一条任务（持久化清理，纯落库删除、不进入消费循环）。返回是否命中删除（不存在返回 false）。</summary>
    Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default);

    /// <summary>分页读取任务（浏览/导出用）。offset 从 0 起；status 非空时按状态过滤；返回本页条目与总数。</summary>
    Task<PagedTaskPool<TItem>> GetPageAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default);
}
