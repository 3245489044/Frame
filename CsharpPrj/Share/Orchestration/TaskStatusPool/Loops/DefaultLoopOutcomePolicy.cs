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
            LoopDecision.Success => TaskExecutionStatus.Completed,
            LoopDecision.Timeout or LoopDecision.TripTerminated => TaskExecutionStatus.Faulted,
            LoopDecision.Continue or LoopDecision.None or LoopDecision.SwitchStrategy => TaskExecutionStatus.Faulted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result), result.FinalDecision, "未知决策，禁默认成功。"),
        };
    }
}
