using Operation.LoopFoldMethod.Rule;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// 规则决策 → 折叠决策结果映射（LLM-free，Agent 无关）—— 把 <see cref="RuleDecision"/>（Continue/Stop/Error）
/// 映为统一 <see cref="DecisionOutcome"/> 词表（输出适配）：Continue → <see cref="DecisionOutcome.Next"/>（软推进 D′）；
/// Stop → <see cref="DecisionOutcome.Stop"/>；Error → <see cref="DecisionOutcome.Fail"/>（显式中止，R7：不回退静默 continue）。
/// 供 Agent 侧工具动作适配（DecisionActionExecutor 等）与规则决策消费者把规则判定收敛到决策词表。
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