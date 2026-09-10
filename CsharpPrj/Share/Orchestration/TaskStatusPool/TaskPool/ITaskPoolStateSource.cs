using System;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务池状态变更事件源（订阅入口）：暴露写即触发的 <see cref="TaskStateChanged"/>。
/// 由 <see cref="ITaskPool{TItem}"/> 实现方或其桥接适配器（如 AgentTaskPoolAdapter）承载，
/// 供 <see cref="TaskPoolStateMirror"/> 等观测方订阅。通用、Agent 无关。
/// </summary>
public interface ITaskPoolStateSource
{
    /// <summary>任务状态变更（写即触发观测）。</summary>
    event EventHandler<TaskPoolStateChangedEventArgs>? TaskStateChanged;
}
