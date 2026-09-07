using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 决策规则引擎核心：MatchWhen（短路）→ Select（优先级/数组序）→ Render（模板）
    /// → Execute（按 OutputStrategy 产出）→ Fallback(default) → Audit。LLM-free，可被任意决策点复用。
    /// </summary>
    public sealed class DecisionRulesEngine
    {
        private readonly IMatchStrategyRegistry _match;
        private readonly IOutputStrategyRegistry _output;
        private readonly ITemplateRenderer _renderer;
        private readonly IRuleAuditSink _audit;
        private readonly IDelegateRegistry? _delegates;

        public DecisionRulesEngine(
            IMatchStrategyRegistry? match = null,
            IOutputStrategyRegistry? output = null,
            ITemplateRenderer? renderer = null,
            IRuleAuditSink? audit = null,
            IDelegateRegistry? delegates = null)
        {
            _match = match ?? new DefaultMatchStrategyRegistry();
            _output = output ?? new DefaultOutputStrategyRegistry();
            _renderer = renderer ?? new PlaceholderTemplateRenderer();
            _audit = audit ?? NullAudit.Instance;
            _delegates = delegates;
        }

        /// <summary>
        /// 评估单条输入→裁决输出。
        /// <paramref name="excludeRuleIds"/>：排除指定规则（LocalLlm 适配器用于"工具未提供时跳过命中规则"零回归）。
        /// </summary>
        public DecisionRuleResult Evaluate(
            DecisionRuleSet? ruleSet,
            DecisionRuleContext ctx,
            IReadOnlyCollection<string>? excludeRuleIds = null,
            object? request = null)
        {
            var rs = ruleSet ?? new DecisionRuleSet();
            var best = SelectBest(rs, ctx, excludeRuleIds);

            if (best is not null)
            {
                var result = Execute(best.Then, ctx, matched: true);
                result.MatchedRule = best;
                _audit.Record(new RuleAuditEntry(rs.Id, best.Id, FirstField(best.When), result.Kind.ToString(), false));
                return result;
            }

            if (rs.Default is not null)
            {
                var result = Execute(rs.Default.Then, ctx, matched: false);
                result.IsFallback = true;
                result.MatchedRule = rs.Default;
                _audit.Record(new RuleAuditEntry(rs.Id, rs.Default.Id, null, result.Kind.ToString(), true));
                return result;
            }

            var empty = new DecisionRuleResult { Matched = false, IsFallback = true, Kind = OutputKind.Text, Text = "" };
            _audit.Record(new RuleAuditEntry(rs.Id, null, null, "text", true));
            return empty;
        }

        /// <summary>
        /// JSON 输入重载（JSON 进/JSON 出）：把单一 JSON 根交给引擎裁决，字段统一按 JSONPath（$.a.b / a.b / context.&lt;key&gt;）
        /// 从根解析。非 LLM 专属，任意 JSON 消费者可直接调用。
        /// </summary>
        public DecisionRuleResult Evaluate(
            DecisionRuleSet? ruleSet,
            JsonObject? input,
            IReadOnlyCollection<string>? excludeRuleIds = null,
            IReadOnlyDictionary<string, object?>? context = null,
            int? index = null)
            => Evaluate(ruleSet, DecisionRuleContext.FromJson(input, context, index), excludeRuleIds, request: input);


        private DecisionRule? SelectBest(
            DecisionRuleSet rs, DecisionRuleContext ctx, IReadOnlyCollection<string>? excludeRuleIds)
        {
            var matches = new List<(DecisionRule Rule, int Order, int Specificity)>();
            var rules = rs.Rules ?? Array.Empty<DecisionRule>();
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule is null) continue;
                if (rule.Id is not null && excludeRuleIds is not null && excludeRuleIds.Contains(rule.Id)) continue;
                if (MatchWhen(rule.When, ctx, rs.CaseSensitive))
                    matches.Add((rule, i, SpecificityOf(rule.When)));
            }
            if (matches.Count == 0) return null;

            if (rs.Priority == RuleSetPriorityMode.Declared)
                return matches
                    .OrderByDescending(m => m.Rule.Priority)
                    .ThenByDescending(m => m.Specificity)
                    .ThenBy(m => m.Order)
                    .First().Rule;

            // ordered：数组序，首个命中优先（LocalLlm 旧行为，零回归）。
            return matches[0].Rule;
        }

        private bool MatchWhen(WhenCondition? when, DecisionRuleContext ctx, bool caseSensitive)
        {
            if (when is null) return true;
            if (when.All is { Count: > 0 } && !when.All.All(c => EvalCondition(c, ctx, caseSensitive)))
                return false;
            if (when.Any is { Count: > 0 } && !when.Any.Any(c => MatchWhen(c, ctx, caseSensitive)))
                return false;
            return true;
        }

        private bool EvalCondition(MatchCondition? cond, DecisionRuleContext ctx, bool caseSensitive)
        {
            if (cond is null) return true;
            if (cond.Match is not null) return MatchClause(cond.Match, ctx, caseSensitive);
            if (cond.Not is not null) return !EvalCondition(cond.Not, ctx, caseSensitive);
            return true;
        }

        private bool MatchClause(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            => _match.Get(clause.Strategy).Match(clause, ctx, caseSensitive);

        private DecisionRuleResult Execute(OutputClause clause, DecisionRuleContext ctx, bool matched)
        {
            var strategy = _output.Get(clause.Type);
            var output = strategy.Render(clause, ctx, _renderer);
            var result = new DecisionRuleResult { Matched = matched, Kind = clause.Type };

            switch (clause.Type)
            {
                case OutputKind.Text:
                case OutputKind.Json:
                    result.Text = output as string ?? "";
                    break;
                case OutputKind.ToolCall:
                    result.ToolCalls = output is RuleToolCall tc ? new[] { tc } : Array.Empty<RuleToolCall>();
                    break;
                case OutputKind.ToolCalls:
                    result.ToolCalls = output as IReadOnlyList<RuleToolCall> ?? Array.Empty<RuleToolCall>();
                    break;
                case OutputKind.Structured:
                    result.Structured = output;
                    break;
                case OutputKind.Merge:
                    // Merge 输出器产出单一 JsonObject（JSON 进/JSON 出：result 即合成后的输出 JSON）。
                    result.Structured = output is JsonNode node ? node.DeepClone() : output;
                    break;
                case OutputKind.Delegate:
                    // 经 IDelegateRegistry 解析委托：注册则执行产出 JSON/对象；未注册保留委托名（零回归，Agent 适配层注入）。
                    var del = _delegates?.Resolve(clause.DelegateName);
                    result.DelegateName = clause.DelegateName;
                    if (del is not null)
                    {
                        try
                        {
                            var produced = del(ctx);
                            result.Structured = produced is JsonNode dn ? dn.DeepClone() : produced;
                        }
                        catch
                        {
                            // 委托执行失败：保留委托名（不吞决策，交由消费方 / 默认裁决处理）。
                        }
                    }
                    break;
            }
            return result;
        }

        private static int SpecificityOf(WhenCondition? when)
        {
            if (when is null) return 0;
            var max = 0;
            if (when.All is not null)
                foreach (var c in when.All)
                    max = Math.Max(max, LeafSpecificity(c));
            return max;
        }

        private static int LeafSpecificity(MatchCondition cond)
            => cond.Match is not null
                ? cond.Match.Strategy switch
                {
                    MatchStrategy.Exact => 7,
                    MatchStrategy.JsonPath => 6,
                    MatchStrategy.Semantic => 6,
                    MatchStrategy.Expression => 6,
                    MatchStrategy.ArrayContains => 6,
                    MatchStrategy.NumericCompare => 6,
                    MatchStrategy.JsonSchema => 7,
                    MatchStrategy.Wildcard => 5,
                    MatchStrategy.Regex => 4,
                    MatchStrategy.StartsWith => 4,
                    MatchStrategy.EndsWith => 4,
                    MatchStrategy.Contains => 3,
                    MatchStrategy.Role => 2,
                    _ => 0,
                }
                : cond.Not is not null ? LeafSpecificity(cond.Not) : 0;

        private static string? FirstField(WhenCondition? when)
            => when?.All?.FirstOrDefault(c => c.Match is not null)?.Match?.Field;

        private sealed class NullAudit : IRuleAuditSink
        {
            public static readonly NullAudit Instance = new();
            public void Record(RuleAuditEntry entry) { }
        }
    }
}

