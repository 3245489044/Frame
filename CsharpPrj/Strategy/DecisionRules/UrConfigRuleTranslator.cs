using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// ur_config（scheduler.rules.default）→ DecisionRuleSet 翻译端口。领域中立纯函数；
    /// 消费方（Config 调度 / 配置式执行）以既有 ur_config 行作为规则源，翻译为统一 <see cref="DecisionRuleSet"/> 后交 <see cref="DecisionPortal"/> 求值。
    /// 语义保真（对齐原 ConfigRuleEngine）：首条命中；数值谓词精确相等；current_strategy Ordinal(大小写敏感)；
    /// last_eval OrdinalIgnoreCase；action 为空的行永不产出提议故丢弃。fail-safe：非法/空 JSON → null。
    /// </summary>
    public interface IConfigRuleTranslator
    {
        /// <summary>翻译 ur_config JSON → 规则集；非法/空返回 null（fail-safe，由调用方决定回退 stay/continue）。</summary>
        DecisionRuleSet? Translate(string urConfigJson);
    }

    /// <summary>把 ur_config 现有规则 schema（snake_case 键，见 ConfigJson 约定）翻译为 DecisionRules 统一规则集的实现。</summary>
    public sealed class UrConfigRuleTranslator : IConfigRuleTranslator
    {
        /// <summary>snapshot 谓词在决策输入中的 JSONPath（对齐 <see cref="DecisionPortal.BuildInput"/> 的 {input:{snapshot}} 约定）。</summary>
        public static class Snapshot
        {
            public const string Round = "$.input.round";
            public const string BudgetLevel = "$.input.budget_level";
            public const string TokenValue = "$.input.token_value";
            public const string ContinuousFailures = "$.input.continuous_failures";
            public const string CurrentStrategy = "$.input.current_strategy";
            public const string LastEval = "$.input.last_eval";
        }

        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        public DecisionRuleSet? Translate(string urConfigJson)
        {
            if (string.IsNullOrWhiteSpace(urConfigJson)) return null;
            UrConfigRuleSetDto? set;
            try
            {
                set = JsonSerializer.Deserialize<UrConfigRuleSetDto>(urConfigJson, DeserializeOptions);
            }
            catch (JsonException)
            {
                return null;
            }
            if (set?.Rules is not { Count: > 0 }) return null;

            var rules = new List<DecisionRule>();
            var index = 0;
            foreach (var src in set.Rules)
            {
                if (src is null) continue;
                // action 为空的行在 Config 语义下永不产出提议（跳过继续匹配）→ 不翻译为可能遮蔽后续规则的命中项。
                if (string.IsNullOrWhiteSpace(src.Action)) continue;
                // 稳定合成 id：源行缺 id 时以顺序索引补位，保证求值结果 RuleId 非空、导演排除续匹配可用（不依赖源行带 id）。
                var id = !string.IsNullOrWhiteSpace(src.Id) ? src.Id : "ur_config_rule_" + index;
                rules.Add(new DecisionRule
                {
                    Id = id,
                    When = BuildWhen(src),
                    Then = BuildThen(src),
                });
                index++;
            }
            if (rules.Count == 0) return null;

            return new DecisionRuleSet
            {
                Id = "ur_config",
                Priority = RuleSetPriorityMode.Ordered,
                // current_strategy 需 Ordinal（大小写敏感）；last_eval 忽略大小写经内联 (?i) 正则表达 → 全局大小写敏感。
                CaseSensitive = true,
                Rules = rules,
            };
        }

        private static WhenCondition BuildWhen(UrConfigRuleDto src)
        {
            var all = new List<MatchCondition>();
            if (src.Round.HasValue) all.Add(EqInt(Snapshot.Round, src.Round.Value));
            if (src.BudgetLevel.HasValue) all.Add(EqInt(Snapshot.BudgetLevel, src.BudgetLevel.Value));
            if (src.TokenValue.HasValue) all.Add(EqInt(Snapshot.TokenValue, src.TokenValue.Value));
            if (src.ContinuousFailures.HasValue) all.Add(EqInt(Snapshot.ContinuousFailures, src.ContinuousFailures.Value));
            if (!string.IsNullOrWhiteSpace(src.CurrentStrategy))
                all.Add(Text(Snapshot.CurrentStrategy, MatchStrategy.Exact, src.CurrentStrategy));
            if (!string.IsNullOrWhiteSpace(src.LastEval))
                all.Add(Text(Snapshot.LastEval, MatchStrategy.Regex, "(?i)^" + System.Text.RegularExpressions.Regex.Escape(src.LastEval) + "$"));
            return all.Count == 0 ? new WhenCondition() : new WhenCondition { All = all };
        }

        private static MatchCondition EqInt(string field, long value)
            => new()
            {
                Match = new MatchClause
                {
                    Field = field,
                    Strategy = MatchStrategy.NumericCompare,
                    Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Options = new Dictionary<string, object?> { ["op"] = "eq" },
                },
            };

        private static MatchCondition Text(string field, MatchStrategy strategy, string value)
            => new()
            {
                Match = new MatchClause { Field = field, Strategy = strategy, Value = value },
            };

        private static OutputClause BuildThen(UrConfigRuleDto src)
        {
            var obj = new JsonObject();
            if (!string.IsNullOrWhiteSpace(src.Action)) obj["action"] = src.Action;
            if (!string.IsNullOrWhiteSpace(src.TargetStrategy)) obj["target_strategy"] = src.TargetStrategy;
            if (!string.IsNullOrWhiteSpace(src.TargetExecutor)) obj["target_executor"] = src.TargetExecutor;
            if (!string.IsNullOrWhiteSpace(src.TargetStatus)) obj["target_status"] = src.TargetStatus;
            if (!string.IsNullOrWhiteSpace(src.Intent)) obj["intent"] = src.Intent;
            if (!string.IsNullOrWhiteSpace(src.Reason)) obj["reason"] = src.Reason;
            obj["confidence"] = src.Confidence is >= 0 and <= 1 ? src.Confidence : 1.0;
            return new OutputClause { Type = OutputKind.Structured, Structured = obj };
        }
    }

    // ---- ur_config DTO（snake_case；映射原 ConfigRuleDto schema）----

    internal sealed class UrConfigRuleSetDto
    {
        public List<UrConfigRuleDto>? Rules { get; set; }
    }

    internal sealed class UrConfigRuleDto
    {
        public string? Id { get; set; }
        public string? Action { get; set; }
        public string? TargetStrategy { get; set; }
        public string? TargetExecutor { get; set; }
        public string? TargetStatus { get; set; }
        public string? Intent { get; set; }
        public string? Reason { get; set; }
        public double Confidence { get; set; } = 1.0;
        public int? Round { get; set; }
        public int? BudgetLevel { get; set; }
        public int? TokenValue { get; set; }
        public int? ContinuousFailures { get; set; }
        public string? CurrentStrategy { get; set; }
        public string? LastEval { get; set; }
    }
}
