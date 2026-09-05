using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Strategy.DecisionRules
{
    /// <summary>匹配策略（枚举化，可经 <see cref="IMatchStrategyRegistry"/> 扩展）。与 clinerules LO.Resolve(table) 语义一致。</summary>
    public enum MatchStrategy
    {
        /// <summary>恒命中（兜底；缺省 when/空 Match 归一）。</summary>
        Always = 0,
        /// <summary>按角色聚合匹配（field=role:&lt;名&gt;，value=子串；value 空=该角色存在即命中）。</summary>
        Role,
        /// <summary>子串包含（大小写敏感度由 RuleSet.CaseSensitive 决定）。默认。</summary>
        Contains,
        /// <summary>正则匹配（value 为正则；写错由 lint 显式报错，不吞）。</summary>
        Regex,
        /// <summary>前缀匹配。</summary>
        StartsWith,
        /// <summary>后缀匹配。</summary>
        EndsWith,
        /// <summary>通配符匹配（* = 任意，? = 单字符）。</summary>
        Wildcard,
        /// <summary>JSONPath 取值比较（field 值为 JSON 文本，value 为待比较串）。复用 Plate.JsonMethod.JsonPathHelper。</summary>
        JsonPath,
        /// <summary>语义近似（预留，对接向量检索，阈值经 options 注入）。</summary>
        Semantic,
        /// <summary>条件表达式求值（value = "context.&lt;key&gt; == &lt;value&gt;" / "context.&lt;key&gt; == null"）。
        /// 复用基础引擎 BasicMethod.DecisionEngine.ConditionEvaluator 对 context.&lt;key&gt; 做取值比较。</summary>
        Expression,
        /// <summary>全等匹配。</summary>
        Exact,
    }

    /// <summary>输出器枚举化（可经 <see cref="IOutputStrategyRegistry"/> 扩展）。</summary>
    public enum OutputKind
    {
        /// <summary>渲染文本（占位符模板）→ 产出文本。</summary>
        Text,
        /// <summary>渲染 JSON（模板/结构化）→ 产出 JSON 文本。</summary>
        Json,
        /// <summary>一次工具调用 → 产出 <see cref="RuleToolCall"/>。</summary>
        [JsonStringEnumMemberName("tool_call")]
        ToolCall,
        /// <summary>多次工具调用 → 产出 <see cref="RuleToolCall"/> 列表。</summary>
        [JsonStringEnumMemberName("tool_calls")]
        ToolCalls,
        /// <summary>结构化对象（非 JSON 字符串）→ 对象透传（供非 LLM 消费方）。</summary>
        Structured,
        /// <summary>委托 C# 函数（动态动作，预留）。</summary>
        Delegate,
    }

    /// <summary>规则集优先级模式：ordered=数组序（LocalLlm 旧行为，零回归）；declared=按 Rule.Priority 权重。</summary>
    public enum RuleSetPriorityMode
    {
        /// <summary>数组序，首个命中优先（LocalLlm 兼容默认）。</summary>
        Ordered,
        /// <summary>显式优先级 + 精确度 + 数组序（对齐 GlobalRuleKey = ⟨Priority, Specificity, Order⟩）。</summary>
        Declared,
    }

    /// <summary>规则条件子句：{ field, strategy, value, options? }。field 从消息/上下文取。</summary>
    public sealed class MatchClause
    {
        /// <summary>取值字段：null/"messages"=完整对话；"system"/"user"/"history"；"role:&lt;名&gt;"；"context.&lt;key&gt;"。</summary>
        public string? Field { get; set; }

        /// <summary>匹配策略（缺省 Contains）。</summary>
        public MatchStrategy Strategy { get; set; } = MatchStrategy.Contains;

        /// <summary>待匹配值（正则/子串/路径等）。</summary>
        public string? Value { get; set; }

        /// <summary>策略扩展选项（如 semantic 阈值、regex 选项）。</summary>
        public IDictionary<string, object?>? Options { get; set; }
    }

    /// <summary>条件叶子：{ match } 或 { not: { match } }；可嵌套。</summary>
    public sealed class MatchCondition
    {
        public MatchClause? Match { get; set; }

        public MatchCondition? Not { get; set; }
    }

    /// <summary>条件组合（谓词 AND/OR/not，语义对齐 TruthValueDomainLogic 与 LO.Assess(conjunction)）。缺省=恒命中。</summary>
    public sealed class WhenCondition
    {
        /// <summary>合取（AND）：全部叶子须命中。</summary>
        public IReadOnlyList<MatchCondition>? All { get; set; }

        /// <summary>析取（OR）：至少一个子条件命中。</summary>
        public IReadOnlyList<WhenCondition>? Any { get; set; }
    }

    /// <summary>输出声明：then.type 决定产出形态。</summary>
    public sealed class OutputClause
    {
        public OutputKind Type { get; set; } = OutputKind.Text;

        /// <summary>text/json：占位符模板。</summary>
        public string? Template { get; set; }

        /// <summary>tool_call：工具名。</summary>
        public string? ToolName { get; set; }

        /// <summary>tool_call：参数 JSON 字符串（缺省 "{}"）。</summary>
        public string? Arguments { get; set; }

        /// <summary>tool_calls：多次工具调用。</summary>
        public IReadOnlyList<OutputClause>? ToolCalls { get; set; }

        /// <summary>structured：对象透传。</summary>
        public object? Structured { get; set; }

        /// <summary>delegate：委托名（经委托注册表解析，预留）。</summary>
        public string? DelegateName { get; set; }
    }

    /// <summary>单条规则：when（条件）→ then（输出）。</summary>
    public sealed class DecisionRule
    {
        public string? Id { get; set; }

        public string? Comment { get; set; }

        /// <summary>显式优先级（越大越先；declared 模式下生效；缺省 0）。</summary>
        public int Priority { get; set; }

        /// <summary>条件（缺省 = 恒命中）。</summary>
        public WhenCondition? When { get; set; }

        /// <summary>输出。</summary>
        public OutputClause Then { get; set; } = new();
    }

    /// <summary>规则集：规则列表 + 兜底 default + 优先级模式。</summary>
    public sealed class DecisionRuleSet
    {
        public string? Id { get; set; }

        public string? Version { get; set; }

        public RuleSetPriorityMode Priority { get; set; } = RuleSetPriorityMode.Ordered;

        public bool CaseSensitive { get; set; }

        public IReadOnlyList<DecisionRule> Rules { get; set; } = Array.Empty<DecisionRule>();

        /// <summary>兜底输出（无命中时）。</summary>
        public DecisionRule? Default { get; set; }
    }

    /// <summary>引擎自有消息形态（LLM-free；不引用 LlmMessage，供其它决策点复用）。</summary>
    public sealed record RuleMessage(string Role, string Content);

    /// <summary>引擎自有工具调用形态（LLM-free）。</summary>
    public sealed record RuleToolCall(string Name, string Arguments);
}
