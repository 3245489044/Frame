
namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 持久化条目 = 任务载荷 + 生命周期状态 + 重试计数。
/// 状态权威 = 运行时 <see cref="TaskStateMachine"/>（内存）；本条目为持久化镜像，供重启回填（<see cref="TaskPool{TItem,TResult}.ProcessPendingAsync"/>）。
/// </summary>
public sealed record TaskPoolEntry<TItem>(TItem Item, TaskExecutionStatus Status, int RetryCount)
    where TItem : ITaskPoolItem;
