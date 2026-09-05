using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Plate.JsonMethod.Utilities;

namespace Strategy.DecisionRules
{
    /// <summary>默认匹配策略实现集（复用 Plate.JsonMethod.JsonPathHelper）。</summary>
    internal static class DefaultMatchStrategies
    {
        public static IMatchStrategy Always { get; } = new AlwaysMatch();
        public static IMatchStrategy Contains { get; } = new ContainsMatch();
        public static IMatchStrategy RegexStrategy { get; } = new RegexMatch();
        public static IMatchStrategy Role { get; } = new RoleMatch();
        public static IMatchStrategy Exact { get; } = new ExactMatch();
        public static IMatchStrategy StartsWith { get; } = new StartsWithMatch();
        public static IMatchStrategy EndsWith { get; } = new EndsWithMatch();
        public static IMatchStrategy Wildcard { get; } = new WildcardMatch();
        public static IMatchStrategy JsonPath { get; } = new JsonPathMatch();
        public static IMatchStrategy Semantic { get; } = new SemanticMatch();
        public static IMatchStrategy Expression { get; } = new ConditionExpressionMatch();

        private abstract class BaseStrategy : IMatchStrategy
        {
            public abstract MatchStrategy Kind { get; }
            public abstract int Specificity { get; }
            public abstract bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive);

            protected static string? Text(MatchClause clause, DecisionRuleContext ctx) => ctx.Field(clause.Field);
            protected static StringComparison Cmp(bool caseSensitive)
                => caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        private sealed class AlwaysMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Always;
            public override int Specificity => 0;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive) => true;
        }

        private sealed class ContainsMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Contains;
            public override int Specificity => 3;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var text = Text(clause, ctx);
                return !string.IsNullOrEmpty(text)
                    && !string.IsNullOrEmpty(clause.Value)
                    && text.IndexOf(clause.Value, Cmp(caseSensitive)) >= 0;
            }
        }

        private sealed class RegexMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Regex;
            public override int Specificity => 4;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var text = Text(clause, ctx);
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(clause.Value)) return false;
                var options = RegexOptions.Singleline;
                if (!caseSensitive) options |= RegexOptions.IgnoreCase;
                try
                {
                    return Regex.IsMatch(text, clause.Value, options);
                }
                catch
                {
                    // 错误正则由 lint 显式报错；此处兜底不命中。
                    return false;
                }
            }
        }

        private sealed class RoleMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Role;
            public override int Specificity => 2;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var role = clause.Field?.StartsWith("role:", StringComparison.OrdinalIgnoreCase) == true
                    ? clause.Field.Substring(5)
                    : clause.Value;
                if (string.IsNullOrEmpty(role)) return false;
                var texts = ctx.RoleContents(role);
                if (texts.Count == 0) return false;
                if (string.IsNullOrWhiteSpace(clause.Value)) return true;
                return texts.Any(t => t.IndexOf(clause.Value, Cmp(caseSensitive)) >= 0);
            }
        }

        private sealed class ExactMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Exact;
            public override int Specificity => 7;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
                => string.Equals(Text(clause, ctx), clause.Value, Cmp(caseSensitive));
        }

        private sealed class StartsWithMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.StartsWith;
            public override int Specificity => 4;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var text = Text(clause, ctx);
                return !string.IsNullOrEmpty(text)
                    && !string.IsNullOrEmpty(clause.Value)
                    && text.StartsWith(clause.Value, Cmp(caseSensitive));
            }
        }

        private sealed class EndsWithMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.EndsWith;
            public override int Specificity => 4;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var text = Text(clause, ctx);
                return !string.IsNullOrEmpty(text)
                    && !string.IsNullOrEmpty(clause.Value)
                    && text.EndsWith(clause.Value, Cmp(caseSensitive));
            }
        }
        private sealed class WildcardMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Wildcard;
            public override int Specificity => 5;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var text = Text(clause, ctx);
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(clause.Value)) return false;
                var pattern = "^" + Regex.Escape(clause.Value)
                    .Replace("\\*", ".*", StringComparison.Ordinal)
                    .Replace("\\?", ".", StringComparison.Ordinal) + "$";
                var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.IsMatch(text, pattern, options);
            }
        }

        private sealed class JsonPathMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.JsonPath;
            public override int Specificity => 6;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
            {
                var text = Text(clause, ctx);
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(clause.Value)) return false;
                try
                {
                    var doc = JsonNode.Parse(text);
                    if (doc is null) return false;
                    var node = JsonPathHelper.ResolveJsonPath(doc, clause.Value);
                    if (node is null) return false;
                    if (clause.Options is not null && clause.Options.TryGetValue("equals", out var eq) && eq is not null)
                    {
                        var nodeText = node.ToJsonString();
                        return string.Equals(nodeText, eq.ToString(), Cmp(caseSensitive));
                    }
                    return true; // 节点存在即命中。
                }
                catch { return false; }
            }
        }

        private sealed class SemanticMatch : BaseStrategy
        {
            public override MatchStrategy Kind => MatchStrategy.Semantic;
            public override int Specificity => 6;
            public override bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
                => false; // 预留：对接向量检索（阈值经 clause.Options 注入）。
        }

    }
}
