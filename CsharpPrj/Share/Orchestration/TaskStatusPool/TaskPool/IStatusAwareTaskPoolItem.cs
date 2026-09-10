
namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 快照驱动任务条目契约（可选）：暴露任务生命周期状态 <see cref="Status"/>，供持久化写通道
/// 把 Status 列与载荷内状态在事务内同步（单一真源，消除「Status 列 / PayloadJson.Status」双写漂移）。
/// 通用任务池（<see cref="TaskPool{TItem,TResult}"/>）对不实现本接口的条目维持「状态机驱动 Status 列」语义不变；
/// 实现本接口的条目（如 Agent 的 <c>AgentTaskItem</c>，任务=快照）则以载荷内 Status 为权威，
/// <c>UpdatePayloadAsync</c> / <c>UpdateStatusAsync</c> 写通道同步派生 Status 列。
/// </summary>
public interface IStatusAwareTaskPoolItem
{
    /// <summary>任务生命周期状态（快照权威）。持久化写通道据此同步 Status 镜像列。</summary>
    TaskExecutionStatus Status { get; set; }
}
