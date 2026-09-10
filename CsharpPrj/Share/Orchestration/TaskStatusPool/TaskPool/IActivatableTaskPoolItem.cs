namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 激活门控条目契约（可选）：带 <see cref="Active"/> 标志的任务条目。
/// 激活与写入解耦 —— 写入（持久化）只负责落库，是否被消费循环采用由 <see cref="Active"/> 标志决定；
/// 消费门在 <see cref="TaskPool{TItem,TResult}.DrainOnceAsync"/> 仅处理「NotStarted 且 Active=true」的任务。
/// 未实现本接口的条目（不具激活语义）恒视为 Active=true（无门控，向后兼容）。
/// </summary>
public interface IActivatableTaskPoolItem
{
    /// <summary>激活标志（持久化）：true=进入消费循环可被采用 / false=仅持久化休眠，不被消费。</summary>
    bool Active { get; set; }
}