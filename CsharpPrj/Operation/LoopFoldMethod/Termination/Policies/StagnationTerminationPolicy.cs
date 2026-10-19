using Modules.Threading;
using System;
using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 空转 → Stagnant（macro 判据连续无推进达上限）。
/// <para>默认仅可见化（report，非终止）；<paramref name="terminate"/> = true 时升级为终止。
/// micro 判据通过不清零 noGain（AX.MacroMicroSeparation）。</para>
/// </summary>
public sealed class StagnationTerminationPolicy : ITerminationPolicy
{
    private readonly bool _terminate;

    public StagnationTerminationPolicy(bool terminate = false) => _terminate = terminate;

    public TerminationVerdict Decide(in TerminationContext context)
    {
        if (context.NoGainLimit <= 0 || context.NoGain < context.NoGainLimit)
            return TerminationVerdict.Continue;

        return _terminate
            ? TerminationVerdict.Terminal(
                LoopDecision.TripTerminated, TerminationReason.Stagnant,
                $"macro 判据连续 {context.NoGain} 轮无推进（上限 {context.NoGainLimit}）")
            : TerminationVerdict.Continue; // 仅可见化：由宿主产出 report，不终止
    }
}
