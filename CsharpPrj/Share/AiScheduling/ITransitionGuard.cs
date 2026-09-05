namespace Share.AiScheduling;

/// <summary>
/// 转移守卫（确定性计算层，策略完全实施）。
/// 校验导演提议是否合法、是否可施加：目标∈可用集（防幻觉）、离散状态合法边、策略不变式/边界 ——
/// 越界的自由探索被拒绝，合法的探索在空间内放行。这是"策略完全实施"的单一落地点。
/// </summary>
public interface ITransitionGuard
{
    TransitionVerdict Verify(StateTransitionProposal proposal, SchedulerDirectorContext context);
}
