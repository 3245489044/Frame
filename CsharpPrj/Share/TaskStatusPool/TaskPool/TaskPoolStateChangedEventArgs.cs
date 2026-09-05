using System;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>任务状态变更事件参数（写即触发观测，供 AgentUi / 工作流 / 订阅者消费）。</summary>
public sealed class TaskPoolStateChangedEventArgs : EventArgs
{
    public TaskPoolStateChangedEventArgs(string taskKey, TaskExecutionStatus fromState, TaskExecutionStatus toState, string? reason = null)
    {
        TaskKey = taskKey;
        FromState = fromState;
        ToState = toState;
        Reason = reason;
        Timestamp = DateTime.UtcNow;
    }

    public string TaskKey { get; }

    public TaskExecutionStatus FromState { get; }

    public TaskExecutionStatus ToState { get; }

    public string? Reason { get; }

    public DateTime Timestamp { get; }
}
