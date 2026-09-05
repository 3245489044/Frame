using Operation.LoopFoldMethod.Rule;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// 规则决策 → 折叠决策结果映射（LLM-free，Agent 无关）—— 使 P2 通用接缝 <see cref="IRuleDecider"/> 的产出
/// （<see cref="RuleDecision"/>，Continue/Stop/Error）可以作为 <see cref="IDecisionSkill{TState,TVertex}"/> 的确定性实现，
/// 与 LLM 协程共用同一个 <see cref="DecisionFoldEngine{TState,TVertex}"/>"规则/LLM 混合决策"（化解多导演/多格式）。
/// Continue → <see cref="DecisionOutcome.Next"/>（软推进 D′）；Stop → <see cref="DecisionOutcome.Stop"/>；
/// Error → <see cref="DecisionOutcome.Fail"/>（显式中止，R7：不回退静默 continue）。
/// </summary>
public static class RuleDecisionToOutcome
{
    /// <summary>RuleDecision → DecisionOutcome；Error → Fail（原因/规则名兜底）。</summary>
    public static DecisionOutcome FromRuleDecision(RuleDecision decision, string continueKey)
        => FromRuleDecision(decision, continueKey, TerminationSignal.Success);

    /// <summary>RuleDecision → DecisionOutcome（Stop 信号可定制，如 Error 侧终止）。</summary>
    public static DecisionOutcome FromRuleDecision(
        RuleDecision decision, string continueKey, TerminationSignal stopSignal)
        => decision.Decision switch
        {
            FoldDecision.Continue => DecisionOutcome.Next(continueKey),
            FoldDecision.Stop => DecisionOutcome.Stop(stopSignal),
            _ => DecisionOutcome.Fail(decision.Reason ?? decision.RuleName ?? "规则决策指向 error"),
        };
}