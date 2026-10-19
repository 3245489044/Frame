using System;
using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>总预算耗尽 → TO（预算终止，引擎内可判定）。</summary>
public sealed class BudgetTerminationPolicy : ITerminationPolicy
{
    public TerminationVerdict Decide(in TerminationContext context)
        => context.HasDeadline && context.Remaining <= TimeSpan.Zero
            ? TerminationVerdict.Terminal(
                LoopDecision.Timeout, TerminationReason.BudgetExhausted,
                $"总预算耗尽（elapsed={context.Elapsed.TotalSeconds:0.#}s）")
            : TerminationVerdict.Continue;
}
