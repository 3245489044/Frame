using System.Text.RegularExpressions;
using WorkFlowMethod.LoopFramework.Abstractions;
using WorkFlowMethod.LoopFramework.Core;

namespace WorkFlowMethod.LoopFramework.StateMachine
{
    public class RuleBasedStateMachine : IStateMachine<LoopState>
    {
        private readonly List<StateRule> _rules;
        private readonly ConditionEvaluatorProxy _conditionEvaluator;
        private LoopState _currentState;

        public LoopState CurrentState => _currentState;

        public string Name => "RuleBased";

        public RuleBasedStateMachine(List<StateRule> rules, LoopState initialState = LoopState.NotStarted)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _currentState = initialState;
            _conditionEvaluator = new ConditionEvaluatorProxy();
        }

        public Task<StateTransition<LoopState>> DecideAsync<TContext>(
            TContext context,
            CancellationToken cancellationToken = default)
        {
            var matchedRules = _rules
                .Where(r => r.FromState.Equals(_currentState) && EvaluateCondition(r.Condition, context))
                .OrderByDescending(r => r.Priority)
                .ToList();

            var bestRule = matchedRules.FirstOrDefault();

            if (bestRule == null)
            {
                return Task.FromResult(new StateTransition<LoopState>
                {
                    NextState = LoopState.Completed,
                    IsCompleted = true,
                    Reasoning = "没有匹配的状态转移规则"
                });
            }

            _currentState = bestRule.ToState;
            return Task.FromResult(new StateTransition<LoopState>
            {
                NextState = bestRule.ToState,
                ActionName = bestRule.ActionName,
                Reasoning = $"规则匹配: {bestRule.Description}"
            });
        }

        public void Reset() => _currentState = LoopState.NotStarted;

        internal void SetCurrentState(LoopState state) => _currentState = state;

        private bool EvaluateCondition<TContext>(string condition, TContext context)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            return _conditionEvaluator.Evaluate(condition, context);
        }
    }

    internal class ConditionEvaluatorProxy
    {
        public bool Evaluate<TContext>(string condition, TContext context)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return false;

            var expr = condition.Trim();

            var parts = expr.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                return parts.All(part => EvaluateSingleCondition(part.Trim(), context));
            }

            return EvaluateSingleCondition(expr, context);
        }

        private bool EvaluateSingleCondition<TContext>(string expr, TContext context)
        {
            var nullMatch = Regex.Match(expr, @"^context\.(\w+)\s*(==|!=)\s*null$", RegexOptions.IgnoreCase);
            if (nullMatch.Success)
            {
                var key = nullMatch.Groups[1].Value;
                var op = nullMatch.Groups[2].Value;
                var keyExists = ContainsKey(context, key);

                return op switch
                {
                    "==" => !keyExists,
                    "!=" => keyExists,
                    _ => false
                };
            }

            var valueMatch = Regex.Match(expr, @"^context\.(\w+)\s*(==|!=)\s*(.+)$", RegexOptions.IgnoreCase);
            if (valueMatch.Success)
            {
                var key = valueMatch.Groups[1].Value;
                var op = valueMatch.Groups[2].Value;
                var expectedValue = valueMatch.Groups[3].Value.Trim().Trim('"', '\'');

                var actualValue = GetActualValue(context, key);

                if (actualValue == null)
                {
                    return op switch
                    {
                        "==" => false,
                        "!=" => true,
                        _ => false
                    };
                }

                return op switch
                {
                    "==" => string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
                    "!=" => !string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
            }

            return false;
        }

        private static bool ContainsKey<TContext>(TContext context, string key)
        {
            if (context is Dictionary<string, object> dictContext)
                return dictContext.ContainsKey(key);

            var prop = typeof(TContext).GetProperty(key);
            if (prop != null) return true;

            var containsMethod = typeof(TContext).GetMethod("ContainsKey", new[] { typeof(string) });
            if (containsMethod != null)
                return (bool)containsMethod.Invoke(context, new object[] { key });

            return false;
        }

        private static string GetActualValue<TContext>(TContext context, string key)
        {
            if (context is Dictionary<string, object> dictContext)
            {
                return dictContext.TryGetValue(key, out var val) ? val?.ToString() : null;
            }

            var prop = typeof(TContext).GetProperty(key);
            if (prop != null)
                return prop.GetValue(context)?.ToString();

            var tryGetMethod = typeof(TContext).GetMethod("TryGet");
            if (tryGetMethod != null && tryGetMethod.IsGenericMethod)
            {
                var genericMethod = tryGetMethod.MakeGenericMethod(typeof(string));
                var parameters = new object[] { key, null };
                var success = (bool)genericMethod.Invoke(context, parameters);
                if (success) return parameters[1]?.ToString();
            }

            return null;
        }
    }
}
