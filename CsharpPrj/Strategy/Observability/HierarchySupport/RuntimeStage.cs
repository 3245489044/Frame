using Share.TaskStatusPool.TaskPool;

namespace Share.HierarchySupport;

/// <summary>
/// 运行时快照 stage 的受控字符串来源（SingleImplementation）—— 消除 AgentRunner 边界发布
/// （"started"/"running"/"completed"）与 AgentTaskPool 状态发布（TaskExecutionStatus.ToString().ToLowerInvariant()）
/// 两套命名源的版本串分裂。所有运行时快照发布的 stage 一律经本类受控常量派生，查询端拿到的 stage 语义单一。
/// </summary>
public static class RuntimeStage
{
    /// <summary>run 启动边界。</summary>
    public const string Started = "started";

    /// <summary>循环执行中（每轮 next 边界）。</summary>
    public const string Running = "running";

    /// <summary>run 终态边界。</summary>
    public const string Completed = "completed";

    /// <summary>空 / 未运行占位。</summary>
    public const string Idle = "idle";

    /// <summary>暂停（状态列写入、非循环内）。</summary>
    public const string Paused = "paused";

    /// <summary>由任务生命周期状态映射 stage（任务池状态列写入路径）：Completed→completed；Paused→paused；Running→running
    /// （含 Paused→Running 的恢复；恢复沿用 running 边界，不再单列）；NotStarted/Cancelled/Faulted → 未运行占位 idle（不产生歧义帧）。</summary>
    public static string FromStatus(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Completed => Completed,
        TaskExecutionStatus.Paused => Paused,
        TaskExecutionStatus.Running => Running,
        _ => Idle,
    };

    /// <summary>归一任意 stage 字符串到受控常量：识别 started/running/completed/paused/idle，其余/空/空白回退 running。</summary>
    public static string Normalize(string? stage) => (stage?.Trim().ToLowerInvariant()) switch
    {
        Started => Started,
        Running => Running,
        Completed => Completed,
        Paused => Paused,
        Idle => Idle,
        _ => Running,
    };
}
