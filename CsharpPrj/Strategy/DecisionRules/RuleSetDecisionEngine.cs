using System;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.DecisionEngine;

namespace Strategy.DecisionRules
{
    /// <summary>声明式规则评估结果（供 IDecisionEngine 适配的 TState 载体）。</summary>
    public sealed record RuleDecisionOutcome(
        string? RuleId,
        OutputKind Kind,
        bool Matched,
        bool IsFallback,
        string? Reasoning = null)
    {
        public static RuleDecisionOutcome Empty { get; } = new(null, OutputKind.Text, false, true);
    }

    /// <summary>
    /// 基础引擎适配器：把声明式 <see cref="DecisionRuleSet"/> 暴露为
    /// <see cref="IDecisionEngine{RuleDecisionOutcome, DecisionRuleContext}"/>，使其可被基础引擎
    /// <see cref="CompositeDecisionEngine{TState,TContext}"/> 组合（文本/JSON/兜底/有状态下一步决策）。
    /// 评估实际委托给 <see cref="DecisionRulesEngine"/>（声明式扩充层），本适配器仅做 IDecisionEngine 契约封装，不重写裁决逻辑。
    /// </summary>
    public sealed class RuleSetDecisionEngine : IDecisionEngine<RuleDecisionOutcome, DecisionRuleContext>
    {
        private readonly DecisionRulesEngine _engine;
        private readonly DecisionRuleSet _ruleSet;
        private RuleDecisionOutcome _current;

        public string Name { get; }

        public RuleDecisionOutcome CurrentState => _current;

        public RuleSetDecisionEngine(
            DecisionRuleSet ruleSet,
            DecisionRulesEngine? engine = null,
            string name = "DecisionRules")
        {
            _ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
            _engine = engine ?? new DecisionRulesEngine();
            Name = name;
            _current = RuleDecisionOutcome.Empty;
        }

        public Task<DecisionResult<RuleDecisionOutcome>> DecideAsync(
            DecisionRuleContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var res = _engine.Evaluate(_ruleSet, context);
            var outcome = new RuleDecisionOutcome(
                res.MatchedRule?.Id,
                res.Kind,
                res.Matched,
                res.IsFallback,
                res.Text);
            _current = outcome;

            var reasoning = outcome.IsFallback
                ? "兜底 default（无命中规则）"
                : $"命中规则 {outcome.RuleId}（{outcome.Kind}）";

            // 底座 CompositeDecisionEngine 契约：IsCompleted=true 表示"未决策、继续下一引擎"；
            // 命中规则或走了兜底 default 均视为"做出了决策"→ false；仅当既无命中也无兜底（res.MatchedRule 为空）→ true。
            var isCompleted = res.MatchedRule is null;

            return Task.FromResult(new DecisionResult<RuleDecisionOutcome>
            {
                NextState = outcome,
                ActionName = outcome.RuleId,
                Reasoning = reasoning,
                IsCompleted = isCompleted,
                IsDeterministic = true,
                Confidence = 1.0,
            });
        }

        public void SetCurrentState(RuleDecisionOutcome state) => _current = state;

        public void Reset() => _current = RuleDecisionOutcome.Empty;
    }
}
