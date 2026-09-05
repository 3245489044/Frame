using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.DecisionEngine
{
    #region 规则驱动决策引擎

    public class RuleBasedDecisionEngine<TState, TContext> : IDecisionEngine<TState, TContext>
    {
        private readonly List<DecisionRule<TState, TContext>> _rules = new();
        private readonly ConditionEvaluator _conditionEvaluator;
        private TState _currentState;
        private readonly TState _initialState;

        public string Name { get; }

        public TState CurrentState => _currentState;

        public int RuleCount => _rules.Count;

        public RuleBasedDecisionEngine(TState initialState, string name = "RuleBased")
        {
            _initialState = initialState;
            _currentState = initialState;
            Name = name;
            _conditionEvaluator = new ConditionEvaluator();
        }

        public RuleBasedDecisionEngine<TState, TContext> AddRule(DecisionRule<TState, TContext> rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            _rules.Add(rule);
            return this;
        }

        public RuleBasedDecisionEngine<TState, TContext> AddRules(IEnumerable<DecisionRule<TState, TContext>> rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            foreach (var rule in rules)
            {
                _rules.Add(rule);
            }
            return this;
        }

        public bool RemoveRule(string ruleName)
        {
            var count = _rules.RemoveAll(r => r.Name == ruleName);
            return count > 0;
        }

        public void ClearRules()
        {
            _rules.Clear();
        }

        public Task<DecisionResult<TState>> DecideAsync(TContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var bestRule = _rules
                    .Where(r => IsRuleMatch(r, context))
                    .OrderByDescending(r => r.Priority)
                    .FirstOrDefault();

                if (bestRule == null)
                {
                    return Task.FromResult(new DecisionResult<TState>
                    {
                        NextState = _currentState,
                        IsCompleted = true,
                        Reasoning = "没有匹配的决策规则"
                    });
                }

                _currentState = bestRule.ToState;

                return Task.FromResult(new DecisionResult<TState>
                {
                    NextState = bestRule.ToState,
                    ActionName = bestRule.ActionName,
                    Reasoning = $"规则匹配: {bestRule.Description}",
                    IsDeterministic = true,
                    Confidence = 1.0
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DecisionExecutionException(Name, _currentState, $"决策过程异常: {ex.Message}");
            }
        }

        public void Reset()
        {
            _currentState = _initialState;
        }

        public void SetCurrentState(TState state)
        {
            _currentState = state;
        }

        private bool IsRuleMatch(DecisionRule<TState, TContext> rule, TContext context)
        {
            if (!IsStateMatch(rule.FromState))
                return false;

            if (rule.Condition != null)
                return rule.Condition(context);

            if (!string.IsNullOrWhiteSpace(rule.ConditionExpression))
                return _conditionEvaluator.Evaluate(rule.ConditionExpression, context);

            return true;
        }

        private bool IsStateMatch(TState state)
        {
            return EqualityComparer<TState>.Default.Equals(state, _currentState);
        }

        public IReadOnlyList<(TState FromState, TState ToState, string? ActionName, string Description)> GetPossibleDecisions()
        {
            return _rules
                .Where(r => IsStateMatch(r.FromState))
                .Select(r => (r.FromState, r.ToState, r.ActionName, r.Description))
                .ToList();
        }
    }

    #endregion
}
