using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 失败立即终止（fail-fast）→ TM。失败语义由宿主按结构事实判定（命令失败/执行异常/判据不合格），
/// 不由文本推测；<see cref="TerminationOptions.HaltOnFailure"/> = false 时不装配本策略。
/// </summary>
public sealed class FailureHaltPolicy : ITerminationPolicy
{
    public TerminationVerdict Decide(in TerminationContext context)
        => context.IsFatal
            ? TerminationVerdict.Terminal(
                LoopDecision.TripTerminated, TerminationReason.Failed, "失败立即终止（HaltOnFailure）")
            : TerminationVerdict.Continue;
}
