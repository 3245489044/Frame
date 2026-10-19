using System;
using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 空闲挂起 → TO（无真实推进心跳超过 idle 窗口）。
/// 心跳只由真实推进驱动，空转/report 轮不得 Beat，否则本策略恒不触发（假活）。
/// </summary>
public sealed class IdleTerminationPolicy : ITerminationPolicy
{
    public TerminationVerdict Decide(in TerminationContext context)
        => context.IdleWindow > TimeSpan.Zero && context.Idle > context.IdleWindow
            ? TerminationVerdict.Terminal(
                LoopDecision.Timeout, TerminationReason.IdleHung,
                $"无真实推进 {context.Idle.TotalSeconds:0.#}s（idle 窗口 {context.IdleWindow.TotalSeconds:0.#}s）")
            : TerminationVerdict.Continue;
}
