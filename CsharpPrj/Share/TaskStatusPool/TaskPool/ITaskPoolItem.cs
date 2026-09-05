
namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务池条目契约（通用任务池 canonical 定义，SingleImplementation / TH-12）。
/// 消费方以任务池为唯一任务状态机，不重复定义任务生命周期。
/// </summary>
public interface ITaskPoolItem
{
    /// <summary>业务幂等键（去重；空值自动回退为 Guid 序列）。</summary>
    string TaskKey { get; }

    /// <summary>优先级（越大越先出队）。</summary>
    int Priority { get; }

    /// <summary>失败最大重试次数。</summary>
    int MaxRetries { get; }
}
