using Modules.Threading;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 统一结果 —— 把「文本成功 / 布尔失败 / 异常」三类失败语义收敛为单一 Outcome
/// （治「业务失败被文本化后当成功」）。成功判据唯一：<see cref="IsSuccess"/>。
/// </summary>
public readonly record struct LoopOutcome(TerminationReason Reason, string? Message)
{
    /// <summary>唯一成功判据（Completed 才算成功；其余一律不成功）。</summary>
    public bool IsSuccess => Reason == TerminationReason.Completed;

    /// <summary>是否为非成功终态。</summary>
    public bool IsTerminalFailure => Reason is not (TerminationReason.None or TerminationReason.Completed);

    public static LoopOutcome Completed(string? message = null)
        => new(TerminationReason.Completed, message);

    public static LoopOutcome Failed(string? message)
        => new(TerminationReason.Failed, message);

    public static LoopOutcome TimedOut(string? message)
        => new(TerminationReason.BudgetExhausted, message);

    public static LoopOutcome IdleHung(string? message)
        => new(TerminationReason.IdleHung, message);

    public static LoopOutcome Cancelled(string? message = null)
        => new(TerminationReason.Cancelled, message);

    public static LoopOutcome HostClosed(string? message = null)
        => new(TerminationReason.HostClosed, message);
}
