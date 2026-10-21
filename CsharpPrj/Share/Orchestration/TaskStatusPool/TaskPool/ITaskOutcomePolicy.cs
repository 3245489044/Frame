namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 通用任务池终态端口（F4）：池不感知具体结果类型，由宿主注入成败投影。
/// 只允许 Completed / Faulted（Cancelled 由取消路径处理）。
/// </summary>
public interface ITaskOutcomePolicy<in TResult>
{
    TaskExecutionStatus ToStatus(TResult result);
}

/// <summary>默认策略：保持既有行为（handler 正常返回即 Completed）。</summary>
public sealed class AlwaysCompletePolicy<TResult> : ITaskOutcomePolicy<TResult>
{
    public static readonly AlwaysCompletePolicy<TResult> Instance = new();

    private AlwaysCompletePolicy() { }

    public TaskExecutionStatus ToStatus(TResult result) => TaskExecutionStatus.Completed;
}
