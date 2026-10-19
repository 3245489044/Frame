using System;
using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 同指纹 N 轮 → 终止（死循环识别，替代「无限 Continue」）。
/// 指纹由宿主提供（结构事实：目标集/判定点的稳定编码），null = 本策略不启用。
/// </summary>
public sealed class FingerprintTerminationPolicy : ITerminationPolicy
{
    private readonly int _limit;

    public FingerprintTerminationPolicy(int limit) => _limit = limit;

    public TerminationVerdict Decide(in TerminationContext context)
    {
        if (_limit <= 0 || string.IsNullOrEmpty(context.Fingerprint))
            return TerminationVerdict.Continue;

        return context.SameFingerprintRounds >= _limit
            ? TerminationVerdict.Terminal(
                LoopDecision.TripTerminated, TerminationReason.Stagnant,
                $"判定点指纹连续 {context.SameFingerprintRounds} 轮不变（上限 {_limit}，fingerprint={context.Fingerprint}）")
            : TerminationVerdict.Continue;
    }
}
