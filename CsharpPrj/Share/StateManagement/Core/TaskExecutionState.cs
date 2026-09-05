namespace Share.StateManagement;

/// <summary>
/// 通用子任务状态追踪器（可复用状态原语，SingleImplementation / TH-12）。
/// 线程安全地承载单个子任务（节点/步骤/工作项）的生命周期：状态、起止时间、错误消息、执行计数。
/// 消费方（如 WorkFlowMethod.Execution.Session.NodeExecutionState）继承本类型并注入具体状态枚举，
/// 不重复实现生命周期追踪。
/// </summary>
/// <typeparam name="TKey">子任务标识（如节点 Key / 步骤 Id）。</typeparam>
/// <typeparam name="TStatus">状态枚举（含初始态=枚举 0 值）。</typeparam>
public class TaskExecutionState<TKey, TStatus>
    where TKey : notnull
    where TStatus : struct, Enum
{
    private readonly object _lock = new();

    /// <summary>子任务标识。</summary>
    public TKey Key { get; }

    /// <summary>当前状态（初始 = 枚举 0 值）。</summary>
    public TStatus Status { get; protected set; }

    public DateTime? StartedAt { get; protected set; }

    public DateTime? CompletedAt { get; protected set; }

    public string? ErrorMessage { get; protected set; }

    public int ExecutionCount { get; protected set; }

    protected TaskExecutionState(TKey key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Status = default;
    }

    /// <summary>
    /// 线程安全状态转移。
    /// </summary>
    /// <param name="to">目标状态。</param>
    /// <param name="guard">转移守卫（对当前状态校验）；返回 false 则不转移。</param>
    /// <param name="markStarted">记录启动时间并递增执行计数（进入运行态时）。</param>
    /// <param name="markCompleted">记录完成时间（进入终态时）。</param>
    /// <param name="error">错误/原因消息（失败/跳过时）。</param>
    protected bool TryTransitionTo(
        TStatus to,
        Func<TStatus, bool>? guard = null,
        bool markStarted = false,
        bool markCompleted = false,
        string? error = null)
    {
        lock (_lock)
        {
            if (guard != null && !guard(Status)) return false;

            if (markStarted)
            {
                StartedAt = DateTime.UtcNow;
                ExecutionCount++;
            }

            Status = to;

            if (markCompleted)
                CompletedAt = DateTime.UtcNow;

            if (error != null)
                ErrorMessage = error;

            return true;
        }
    }

    /// <summary>重置为初始态（不清空执行计数）。</summary>
    public void Reset()
    {
        lock (_lock)
        {
            Status = default;
            StartedAt = null;
            CompletedAt = null;
            ErrorMessage = null;
        }
    }
}
