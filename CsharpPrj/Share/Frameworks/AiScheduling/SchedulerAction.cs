namespace Share.AiScheduling;

/// <summary>
/// 调度动作域 —— AI 混合调度（LLM/配置提议 + 计算层校验）可施加的整体状态变更动作。
/// 由 <see cref="IStateDirector"/>（提议源）提出、<see cref="ITransitionGuard"/>（确定性计算层）校验后落地。
/// 非限定：本枚举不携带任何终止语义（继续/终止由外部裁决）。
/// </summary>
public enum SchedulerAction
{
    /// <summary>保持当前状态继续（默认兜底动作）。</summary>
    Continue,

    /// <summary>切换当前策略到 <see cref="StateTransitionProposal.TargetStrategyId"/>。</summary>
    SwitchStrategy,

    /// <summary>推进/登记一个运行中的执行到目标离散状态（<see cref="StateTransitionProposal.TargetRunStatusId"/>）。</summary>
    AdvanceRun,

    /// <summary>将某执行挂起（Paused）。</summary>
    PauseRun,

    /// <summary>重试上一执行（进度/熔断推进）。</summary>
    Retry,

    /// <summary>重新规划（探索性意图：更换方向/目标摘要）。</summary>
    Replan,

    /// <summary>探索性动作（自由探索：调整意图/边界/方向，不改变离散终态）。</summary>
    Explore,
}
