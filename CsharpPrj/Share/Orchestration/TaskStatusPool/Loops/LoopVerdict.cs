namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 唯一终态（AX-ZR BinaryOutcome）：<c>Complete</c> ⇔ <see cref="StepOutcome.Ok"/>；
/// <c>Abort</c> ⇔ <see cref="StepOutcome.Fail"/>（含取消）。<c>None</c> 仅表示「尚未裁决」。
/// 旧 <see cref="LoopDecision"/> 只在适配器/遥测投影中出现，禁入控制流条件位。
/// </summary>
public enum LoopVerdict
{
    None,
    Complete,
    Abort,
}

/// <summary><see cref="LoopVerdict"/> 的唯一派生点（禁各自手写三态映射）。</summary>
public static class LoopVerdicts
{
    public static LoopVerdict From(StepOutcome outcome)
        => outcome is StepOutcome.Ok ? LoopVerdict.Complete : LoopVerdict.Abort;

    /// <summary>旧决策域 → 唯一终态（适配器专用；<c>Continue/None</c> ⇒ <see cref="LoopVerdict.None"/>）。</summary>
    public static LoopVerdict FromLegacy(LoopDecision decision)
        => decision switch
        {
            LoopDecision.Complete or LoopDecision.Success => LoopVerdict.Complete,
            LoopDecision.Abort or LoopDecision.Timeout or LoopDecision.TripTerminated => LoopVerdict.Abort,
            _ => LoopVerdict.None,
        };
}
