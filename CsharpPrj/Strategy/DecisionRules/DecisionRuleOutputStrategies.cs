using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.DecisionRules
{
    /// <summary>默认输出策略实现集。</summary>
    internal static class DefaultOutputStrategies
    {
        public static IOutputStrategy Text { get; } = new TextOutput();
        public static IOutputStrategy Json { get; } = new JsonOutput();
        public static IOutputStrategy ToolCall { get; } = new ToolCallOutput();
        public static IOutputStrategy ToolCalls { get; } = new ToolCallsOutput();
        public static IOutputStrategy Structured { get; } = new StructuredOutput();
        public static IOutputStrategy Delegate { get; } = new DelegateOutput();

        private sealed class TextOutput : IOutputStrategy
        {
            public OutputKind Kind => OutputKind.Text;
            public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
                => renderer.Render(clause.Template, ctx);
        }

        private sealed class JsonOutput : IOutputStrategy
        {
            public OutputKind Kind => OutputKind.Json;
            public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
            {
                var text = renderer.Render(clause.Template, ctx);
                if (!string.IsNullOrEmpty(text)) return text;
                if (clause.Structured is not null)
                {
                    try { return System.Text.Json.JsonSerializer.Serialize(clause.Structured); }
                    catch { return "{}"; }
                }
                return "{}";
            }
        }

        private sealed class ToolCallOutput : IOutputStrategy
        {
            public OutputKind Kind => OutputKind.ToolCall;
            public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
                => new RuleToolCall(clause.ToolName ?? "", clause.Arguments ?? "{}");
        }

        private sealed class ToolCallsOutput : IOutputStrategy
        {
            public OutputKind Kind => OutputKind.ToolCalls;
            public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
            {
                if (clause.ToolCalls is not { Count: > 0 }) return Array.Empty<RuleToolCall>();
                return clause.ToolCalls
                    .Select(c => new RuleToolCall(c.ToolName ?? "", c.Arguments ?? "{}"))
                    .ToArray();
            }
        }

        private sealed class StructuredOutput : IOutputStrategy
        {
            public OutputKind Kind => OutputKind.Structured;
            public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
                => clause.Structured;
        }

        private sealed class DelegateOutput : IOutputStrategy
        {
            public OutputKind Kind => OutputKind.Delegate;
            public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
                => clause.DelegateName;
        }
    }

    /// <summary>默认匹配策略注册表（可替换扩展）。</summary>
    public sealed class DefaultMatchStrategyRegistry : IMatchStrategyRegistry
    {
        private readonly Dictionary<MatchStrategy, IMatchStrategy> _map = new()
        {
            [MatchStrategy.Always] = DefaultMatchStrategies.Always,
            [MatchStrategy.Contains] = DefaultMatchStrategies.Contains,
            [MatchStrategy.Regex] = DefaultMatchStrategies.RegexStrategy,
            [MatchStrategy.Role] = DefaultMatchStrategies.Role,
            [MatchStrategy.Exact] = DefaultMatchStrategies.Exact,
            [MatchStrategy.StartsWith] = DefaultMatchStrategies.StartsWith,
            [MatchStrategy.EndsWith] = DefaultMatchStrategies.EndsWith,
            [MatchStrategy.Wildcard] = DefaultMatchStrategies.Wildcard,
            [MatchStrategy.JsonPath] = DefaultMatchStrategies.JsonPath,
            [MatchStrategy.Semantic] = DefaultMatchStrategies.Semantic,
            [MatchStrategy.Expression] = DefaultMatchStrategies.Expression,
        };

        public IMatchStrategy Get(MatchStrategy strategy)
        {
            if (_map.TryGetValue(strategy, out var s)) return s;
            return DefaultMatchStrategies.Always;
        }
    }

    /// <summary>默认输出策略注册表（可替换扩展）。</summary>
    public sealed class DefaultOutputStrategyRegistry : IOutputStrategyRegistry
    {
        private readonly Dictionary<OutputKind, IOutputStrategy> _map = new()
        {
            [OutputKind.Text] = DefaultOutputStrategies.Text,
            [OutputKind.Json] = DefaultOutputStrategies.Json,
            [OutputKind.ToolCall] = DefaultOutputStrategies.ToolCall,
            [OutputKind.ToolCalls] = DefaultOutputStrategies.ToolCalls,
            [OutputKind.Structured] = DefaultOutputStrategies.Structured,
            [OutputKind.Delegate] = DefaultOutputStrategies.Delegate,
        };

        public IOutputStrategy Get(OutputKind kind)
        {
            if (_map.TryGetValue(kind, out var s)) return s;
            return DefaultOutputStrategies.Text;
        }
    }
}
