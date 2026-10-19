using System;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 终止判定快照（只读）—— 所有策略的唯一入参，避免策略各自抓时钟/抓状态。
/// 时间维度来自 <see cref="Modules.Threading.TerminationScope"/>，推进维度由 <see cref="LoopRunner{TState,TResult}"/>
/// 记账（noGain / 同指纹轮数），macro 判据层级口径见 clinerules AX.MacroMicroSeparation。
/// </summary>
public readonly record struct TerminationContext(
    string ScopeId,
    string Owner,
    int Round,
    TimeSpan Elapsed,
    TimeSpan Idle,
    TimeSpan IdleWindow,
    TimeSpan Remaining,
    bool HasDeadline,
    bool IsFatal,
    string? Fingerprint,
    int SameFingerprintRounds,
    int NoGain,
    int NoGainLimit,
    bool HostClosed = false);
