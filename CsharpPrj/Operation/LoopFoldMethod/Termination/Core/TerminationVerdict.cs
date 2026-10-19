using Modules.Threading;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 终止裁决值 —— 决策（what，<see cref="LoopDecision"/>）+ 理由（why，<see cref="TerminationReason"/>）
/// + 证据（可审计）+ 是否终值。非终值只有 <see cref="Continue"/>，禁止用「继续」表达任何降级。
/// </summary>
public readonly record struct TerminationVerdict(
    LoopDecision Decision,
    TerminationReason Reason,
    bool IsTerminal,
    string Evidence)
{
    /// <summary>继续推进（非终值）。</summary>
    public static TerminationVerdict Continue { get; } =
        new(LoopDecision.Continue, TerminationReason.None, false, string.Empty);

    /// <summary>构造终值裁决。</summary>
    public static TerminationVerdict Terminal(LoopDecision decision, TerminationReason reason, string evidence)
        => new(decision, reason, true, evidence);
}
