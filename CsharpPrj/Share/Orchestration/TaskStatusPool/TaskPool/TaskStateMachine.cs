using System;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务级状态机（自包含，通用任务池 canonical）。轻量实现：CurrentState + 带守卫转移。
/// 生命周期：NotStarted → Running ↔ Paused → Completed / Faulted / Cancelled。
/// 转移守卫语义对齐原 ExecutionExtensions（Share.StateManagement）对 ExecutionStatus 的转移规则。
/// </summary>
public sealed class TaskStateMachine
{
    private TaskExecutionStatus _current;
    private readonly object _lock = new();

    public TaskStateMachine(TaskExecutionStatus initialState = TaskExecutionStatus.NotStarted)
        => _current = initialState;

    public TaskExecutionStatus CurrentState
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>带谓词守卫的转移（predicate 对当前状态求值，不满足返回 false）。</summary>
    public bool TryTransitionTo(TaskExecutionStatus newState, Func<TaskExecutionStatus, bool>? condition = null)
    {
        lock (_lock)
        {
            if (condition != null && !condition(_current))
                return false;
            _current = newState;
            return true;
        }
    }

    /// <summary>期望当前状态精确匹配的转移（不匹配返回 false）。</summary>
    public bool TryTransitionFrom(TaskExecutionStatus expected, TaskExecutionStatus newState)
    {
        lock (_lock)
        {
            if (_current != expected) return false;
            _current = newState;
            return true;
        }
    }

    // ---- 具名转移（对齐原 ExecutionExtensions 语义）----

    /// <summary>NotStarted → Running。</summary>
    public bool TryStart() => TryTransitionFrom(TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running);

    /// <summary>Running → Paused（非终止）。</summary>
    public bool TryPause() => TryTransitionFrom(TaskExecutionStatus.Running, TaskExecutionStatus.Paused);

    /// <summary>Paused → Running（非终止）。</summary>
    public bool TryResume() => TryTransitionFrom(TaskExecutionStatus.Paused, TaskExecutionStatus.Running);

    /// <summary>→ Completed（禁止从 Completed/Faulted 终态迁移）。</summary>
    public bool TryComplete() => TryTransitionTo(TaskExecutionStatus.Completed,
        s => s != TaskExecutionStatus.Completed && s != TaskExecutionStatus.Faulted);

    /// <summary>→ Faulted（禁止从 Completed 终态迁移）。</summary>
    public bool TryFault() => TryTransitionTo(TaskExecutionStatus.Faulted,
        s => s != TaskExecutionStatus.Completed);

    /// <summary>→ Cancelled（禁止从 Completed/Faulted/Cancelled 终态迁移）。</summary>
    public bool TryCancel() => TryTransitionTo(TaskExecutionStatus.Cancelled,
        s => s != TaskExecutionStatus.Completed
          && s != TaskExecutionStatus.Faulted
          && s != TaskExecutionStatus.Cancelled);
}
