using System;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>任务状态变更事件参数（写即触发观测，供 AgentUi / 工作流 / 订阅者消费）。</summary>
public sealed class TaskPoolStateChangedEventArgs : EventArgs
{
    public TaskPoolStateChangedEventArgs(
        string taskKey,
        TaskExecutionStatus fromState,
        TaskExecutionStatus toState,
        string? reason = null,
        TaskPoolItemKind kind = TaskPoolItemKind.Executable,
        bool active = true,
        string? leaseId = null)
    {
        TaskKey = taskKey;
        FromState = fromState;
        ToState = toState;
        Reason = reason;
        Kind = kind;
        Active = active;
        LeaseId = leaseId;
        Timestamp = DateTime.UtcNow;
    }

    public string TaskKey { get; }

    public TaskExecutionStatus FromState { get; }

    public TaskExecutionStatus ToState { get; }

    public string? Reason { get; }

    /// <summary>条目语义类别（只有 Executable 参与刷新心跳）。</summary>
    public TaskPoolItemKind Kind { get; }

    /// <summary>条目激活标志（false = 休眠簿记，永不唤醒刷新心跳）。</summary>
    public bool Active { get; }

    /// <summary>租约键（可空；执行条目可由上层登记）。</summary>
    public string? LeaseId { get; }

    public DateTime Timestamp { get; }
}
