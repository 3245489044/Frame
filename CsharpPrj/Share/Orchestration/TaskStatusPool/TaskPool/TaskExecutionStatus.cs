
namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务池任务生命周期状态（通用任务池 canonical，Agent 无关）。
/// 语义对齐 ExecutionStateMachine 的执行生命周期（NotStarted → Running ↔ Paused → Completed / Faulted / Cancelled）。
/// 独立定义于通用基础框架，避免与 Modules.StateMachine.ExecutionStatus / Share.StateManagement.ExecutionStatus 重名歧义。
/// </summary>
public enum TaskExecutionStatus
{
    /// <summary>已入队未开始（可出队）。</summary>
    NotStarted,

    /// <summary>在途执行中。</summary>
    Running,

    /// <summary>暂停（循环内非终止，Running ⇄ Paused）。</summary>
    Paused,

    /// <summary>成功完成。</summary>
    Completed,

    /// <summary>失败（耗尽重试预算）。</summary>
    Faulted,

    /// <summary>取消（终止域）。</summary>
    Cancelled
}
