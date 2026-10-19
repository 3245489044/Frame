using Modules.Threading;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 宿主关闭 → HostClosed（探索终止，不落预算裁决）。
/// 只需在策略链最前短路：关窗后不再执行任何命令/轮次，且终态如实上报 external_termination。
/// </summary>
public sealed class HostClosedPolicy : ITerminationPolicy
{
    public TerminationVerdict Decide(in TerminationContext context)
        => context.HostClosed
            ? TerminationVerdict.Terminal(
                // 决策位仅作占位（探索终止由 Reason 承载，不得被当作预算终止外泄）。
                Share.TaskStatusPool.Loops.LoopDecision.TripTerminated,
                TerminationReason.HostClosed,
                "宿主已关闭（关窗/进程退出）")
            : TerminationVerdict.Continue;
}
