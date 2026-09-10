namespace Share.AiScheduling;

/// <summary>
/// 转移校验裁决（确定性计算层 <see cref="ITransitionGuard"/> 的产出）。
/// Pass=可施加（改变）/ Held=目标与当前一致（保持）/ Reject=非法或越出策略不变式（回退确定性默认动作）。
/// </summary>
public enum TransitionVerdict
{
    /// <summary>校验通过，可施加（改变）。</summary>
    Pass,

    /// <summary>目标与当前一致（保持，Held）。</summary>
    Held,

    /// <summary>校验拒绝（非法转移 / 越界 / 策略不变式违规）→ 回退确定性默认动作。</summary>
    Reject,
}
