using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// LLM 软决策协程端口（决策的 LLM 部分 → skill）。async 协程（可挂起/恢复），消费当前状态 + 驱动信号 + 折叠上下文，
/// 产出下一步折叠链标识符 D′（<see cref="DecisionOutcome.ContinueTo"/>）或链终点/终止（<see cref="DecisionOutcome.Terminate"/>）。
/// 确定性硬性部分由 <see cref="IDeterministicTermination{TState}"/> 引擎内裁决，不经本端口
/// （SEP.DeterministicCoreDisjoint：skill 只产出 D′、不触碰确定性核心）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public interface IDecisionSkill<TState, TVertex>
{
    /// <summary>执行一步决策：读状态 → 决策 → 产出下一状态点 + 结论 + 决策结果（D′ 或终止）。</summary>
    Task<DecisionStep<TState, TVertex>> DecideAsync(
        TState state,
        FoldSignal<TVertex> signal,
        FoldContext<TVertex> ctx,
        CancellationToken ct = default);
}
