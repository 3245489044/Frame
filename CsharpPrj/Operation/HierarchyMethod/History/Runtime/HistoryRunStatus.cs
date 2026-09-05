namespace Operation.HierarchyMethod.History.Runtime;

/// <summary>
/// 单个 task 的运行状态快照（History 编排层通用 DTO，词汇中立）。
/// 承载「任务池状态（TaskExecutionStatus）」与「子智能体执行状态（AgentRunStatus）」两维度文本，
/// 由注入端口 <see cref="IHistoryRuntimePort"/> 的 Agent 适配器填充；本框架本体零 Agent / 零具体状态枚举依赖。
/// </summary>
public sealed record HistoryTaskRunStatus(
    string TaskKey,
    /// <summary>执行器标识（如 cline / stateless-answer）；null = 未解析/纯簿记。</summary>
    string? ExecutorId = null,
    /// <summary>任务池状态文本（NotStarted/Running/Paused/Completed/Faulted/Cancelled 等）。</summary>
    string? Status = null,
    /// <summary>子智能体执行状态文本（Pending/Running/Done/Error/Paused 等）。</summary>
    string? ExecutorStatus = null,
    /// <summary>当前轮次（Round）。</summary>
    int Round = 0,
    /// <summary>激活标志（Active=true 才进入消费循环）。</summary>
    bool Active = false,
    /// <summary>最近一次循环决策（decision 文本，可选）。</summary>
    string? Decision = null);
