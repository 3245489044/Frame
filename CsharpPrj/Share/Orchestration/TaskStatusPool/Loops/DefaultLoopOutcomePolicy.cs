using System;
using Share.TaskStatusPool.TaskPool;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 循环终态投影策略：结果成败 → 池终态（Completed/Faulted）。穷举决策值，禁默认成功。
/// </summary>
public sealed class DefaultLoopOutcomePolicy : ITaskOutcomePolicy<AxiomLoopResult>
{
    public static readonly DefaultLoopOutcomePolicy Instance = new();

    private DefaultLoopOutcomePolicy() { }

    public TaskExecutionStatus ToStatus(AxiomLoopResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.FinalDecision switch
        {
            LoopDecision.Complete or LoopDecision.Success => TaskExecutionStatus.Completed,
            LoopDecision.Abort or LoopDecision.Timeout or LoopDecision.TripTerminated => TaskExecutionStatus.Faulted,
            // Continue/None/SwitchStrategy：二值终止下不可表达（未赋值/第三态）⇒ fail-fast，禁默认成功。
            _ => throw new ArgumentOutOfRangeException(
                nameof(result), result.FinalDecision, "决策未赋值或为第三态，禁默认成功。"),
        };
    }
}
