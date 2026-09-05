using System;
using System.Collections.Generic;
using System.Text.Json;
using Modules.Parsing.Rules;

namespace Operation.HierarchyMethod.Rules
{
    /// <summary>循环守卫（max_steps 保证终止）。</summary>
    public sealed class GuardConfig
    {
        public int MaxSteps { get; set; } = 10;
    }

    /// <summary>单条循环规则；Condition 为 JSON（字符串 "true"/"false" 或对象 {field,op,value}）。</summary>
    public sealed class LoopRule
    {
        public string Name { get; set; } = "";
        public JsonElement Condition { get; set; }
        public string Action { get; set; } = "";
        public string? Reason { get; set; }
        public string? NextPromptHint { get; set; }
    }

    /// <summary>
    /// 循环规则集（纯数据模型，LLM-free）—— 下沉自 Agent.Core（SingleImplementation / TH-12）。
    /// 加载/兜底/fail-fast（Config 来源、内嵌默认、日志）由 Agent 应用层 <c>RuleSetLoader</c> 承担，
    /// 框架本体不承载配置来源与日志，保持零 Agent 依赖。
    /// </summary>
    public sealed class RuleSet
    {
        public string Version { get; set; } = "1.0.0";
        public GuardConfig Guards { get; set; } = new();
        public List<LoopRule> Rules { get; set; } = new();
    }

    /// <summary>规则评估结果（含路由决策）。</summary>
    public sealed class RuleResult
    {
        public LoopRule? Rule { get; }
        public string Action { get; }
        public string? Reason { get; }
        public string? NextPromptHint { get; }

        public RuleResult(LoopRule? rule, string action, string? reason, string? hint)
        {
            Rule = rule; Action = action; Reason = reason; NextPromptHint = hint;
        }

        public static RuleResult FromRule(LoopRule r) => new(r, r.Action, r.Reason, r.NextPromptHint);
        public static RuleResult Stop(string? reason, LoopRule? rule = null) => new(rule, "stop", reason, null);
    }

    /// <summary>
    /// 确定性规则评估引擎（零 LLM）—— 下沉自 Agent.Core（SingleImplementation / TH-12）。
    /// 字段经 <see cref="JsonRuleEngine"/>（Modules.Parsing.Rules）解析点路径；同一引擎服务
    /// rule_match 与 provide_prompt 的 rule_needs_replan 判定。
    /// 未知 op 经 <paramref name="onUnknownOperator"/> 回调暴露配置错误（缺省 no-op；Agent 侧承载日志）。
    /// </summary>
    public static class RuleEngine
    {
        /// <summary>缺省最大步数守卫（对齐 <see cref="GuardConfig.MaxSteps"/>；无显式规则集时的守卫值）。</summary>
        public const int DefaultMaxSteps = 10;

        /// <summary>对规则集首匹配评估（条件经 <see cref="JsonRuleEngine"/> 求值）；超步数守卫返回 continue 交由预算终止裁决。</summary>
        public static RuleResult Evaluate(RuleSet rs, JsonElement data, int stepCount, Action<string, string?>? onUnknownOperator = null)
        {
            // E5 包含式：max_steps 为「最大允许规则求值步数」（包含 1..MaxSteps），步数 &gt; MaxSteps 才超出上限。
            // 达到最大步数守卫 ≠ 任务完成。返回 continue（非 stop 成功），由循环继续直至预算终止（TO）裁决，
            // 避免「未完成被静默记为成功（TS）」。rule_match 产出 decision=continue → ok 判定按 continue → Partial。
            if (!IsWithinGuard(stepCount, rs.Guards.MaxSteps))
            {
                return new RuleResult(null, "continue", $"达到最大步数守卫（{rs.Guards.MaxSteps}），交由预算终止裁决", null);
            }

            foreach (var rule in rs.Rules)
            {
                if (JsonRuleEngine.EvaluateCondition(rule.Condition, data, onUnknownOperator)) return RuleResult.FromRule(rule);
            }
            return RuleResult.Stop("默认终止（规则集无命中）");
        }

        /// <summary>
        /// 步数守卫判定（单一权威，对齐 E3/E5 守卫语义）：stepCount 未超过 MaxSteps（&lt;= MaxSteps）即"在守卫内"，
        /// 步数超过 MaxSteps 才触发 max_steps 守卫。stepCount 由调用方从 1 起（0/负亦视为守卫内，仅上限约束）。
        /// 供规则引擎与 rule_match 调用方共用，避免阈值语义双写漂移。
        /// </summary>
        public static bool IsWithinGuard(int stepCount, int? maxSteps = null)
            => stepCount <= (maxSteps ?? DefaultMaxSteps);

        /// <summary>单条 field/op/value 判定（委托 <see cref="JsonRuleEngine"/>）。</summary>
        public static bool Evaluate(JsonElement data, string? field, string op, JsonElement value, Action<string, string?>? onUnknownOperator = null)
            => JsonRuleEngine.Evaluate(data, field, op, value, onUnknownOperator);
    }
}
