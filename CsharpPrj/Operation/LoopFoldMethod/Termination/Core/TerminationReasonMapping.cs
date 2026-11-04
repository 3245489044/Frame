using System;
using Modules.Threading;
using Operation.LoopFoldMethod.Fold;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 终止理由 ↔ 既有终态词表的**唯一映射**（消除 FoldCut / TerminationSignal / TerminationKind 的语义漂移）。
/// <para>域约束（AX.TerminationBipartition）：<see cref="TerminationReason.Cancelled"/> /
/// <see cref="TerminationReason.HostClosed"/> 属探索终止，**不得**映射为预算术语义；
/// 反向调用会显式抛错，防止被静默吞成 Continue/Timeout。</para>
/// </summary>
public static class TerminationReasonMapping
{
    /// <summary>理由 → 引擎预算终止值（仅预算终止域合法）。</summary>
    public static LoopDecision ToLoopDecision(TerminationReason reason) => reason switch
    {
        TerminationReason.Completed => LoopDecision.Success,
        TerminationReason.BudgetExhausted => LoopDecision.Timeout,
        TerminationReason.IdleHung => LoopDecision.Timeout,
        TerminationReason.Failed => LoopDecision.TripTerminated,
        TerminationReason.Stagnant => LoopDecision.TripTerminated,
        TerminationReason.None => LoopDecision.Continue,
        TerminationReason.Cancelled or TerminationReason.HostClosed =>
            throw new InvalidOperationException(
                $"TerminationReason.{reason} 属探索终止（外部取消/宿主关闭），不得映射为 LoopDecision 预算裁决；" +
                "应由运行结果按外部终止承载。"),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>理由 → 折叠截断值。</summary>
    public static FoldCut ToFoldCut(TerminationReason reason) => reason switch
    {
        TerminationReason.Completed => FoldCut.Success,
        TerminationReason.BudgetExhausted => FoldCut.BudgetExhausted,
        TerminationReason.IdleHung => FoldCut.BudgetExhausted,
        TerminationReason.Failed => FoldCut.TripTerminated,
        TerminationReason.Stagnant => FoldCut.TripTerminated,
        TerminationReason.Cancelled => FoldCut.Cancelled,
        TerminationReason.HostClosed => FoldCut.Cancelled,
        TerminationReason.None => FoldCut.Continue,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>折叠截断值 → 理由（Continue 域回 None）。</summary>
    public static TerminationReason FromFoldCut(FoldCut cut) => cut switch
    {
        FoldCut.Success => TerminationReason.Completed,
        FoldCut.BudgetExhausted => TerminationReason.BudgetExhausted,
        FoldCut.TripTerminated => TerminationReason.Failed,
        FoldCut.Cancelled => TerminationReason.Cancelled,
        _ => TerminationReason.None,
    };

    /// <summary>
    /// 引擎终态 → 理由（Runner 收尾的单一映射权威）。
    /// 注：与 <see cref="FromFoldCut"/> 同属**非双射**（TripTerminated/Failed 与 Stagnant 均归 Failed），
    /// 禁止用于「理由 → 截断值 → 理由」往返还原。
    /// </summary>
    public static TerminationReason FromLoopDecision(LoopDecision decision) => decision switch
    {
        LoopDecision.Success or LoopDecision.Complete => TerminationReason.Completed,
        LoopDecision.Timeout => TerminationReason.BudgetExhausted,
        LoopDecision.TripTerminated => TerminationReason.Failed,
        LoopDecision.Abort => TerminationReason.Failed,
        _ => TerminationReason.None,
    };

    /// <summary>
    /// 理由 → 统一结果。None 表示「尚非终态」，不得伪装成失败（E-09：避免 IsTerminalFailure 与实际 Reason 自相矛盾）。
    /// </summary>
    public static LoopOutcome ToOutcome(TerminationReason reason, string? message = null) => reason switch
    {
        TerminationReason.None => new LoopOutcome(TerminationReason.None, message),
        TerminationReason.Completed => LoopOutcome.Completed(message),
        TerminationReason.BudgetExhausted => LoopOutcome.TimedOut(message),
        TerminationReason.IdleHung => LoopOutcome.IdleHung(message),
        TerminationReason.Cancelled => LoopOutcome.Cancelled(message),
        TerminationReason.HostClosed => LoopOutcome.HostClosed(message),
        _ => LoopOutcome.Failed(message),
    };
}
