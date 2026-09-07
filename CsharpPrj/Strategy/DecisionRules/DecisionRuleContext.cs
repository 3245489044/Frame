using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Strategy.DecisionRules
{
    /// <summary>引擎评估上下文（LLM-free，含消息/系统/用户/历史/上下文字典）。</summary>
    public sealed class DecisionRuleContext
    {
        public string? System { get; set; }

        public string? User { get; set; }

        public IReadOnlyList<RuleMessage>? History { get; set; }

        public IReadOnlyList<RuleMessage>? Messages { get; set; }

        /// <summary>context.&lt;key&gt; 取值字典。</summary>
        public IReadOnlyDictionary<string, object?>? Context { get; set; }

        /// <summary>轮次索引（渲染占位符 {index}）。</summary>
        public int? Index { get; set; }
        /// <summary>可选 JSON 根（JSON 进/JSON 出）：非空时 field 可写 JSONPath（$.a.b / $ / a.b），
        /// 且 context.&lt;key&gt; 归一到 $.context.&lt;key&gt;；空时保持旧 LLM 形状取值（零回归）。</summary>
        public JsonNode? Root { get; set; }

        /// <summary>构造 JSON 进上下文（根即决策输入）。</summary>
        public static DecisionRuleContext FromJson(JsonNode? root, IReadOnlyDictionary<string, object?>? context = null, int? index = null)
            => new() { Root = root, Context = context, Index = index };



        /// <summary>取指定字段文本：null/"messages"=完整对话；"system"/"user"/"history"；"role:&lt;名&gt;"；"context.&lt;key&gt;"。</summary>
        public string? Field(string? field)
        {
            // JSON 根优先：JSONPath（$ / $.a.b / a.b / context.<key> → $.context.<key>）。
            if (Root is not null && !string.IsNullOrEmpty(field))
            {
                if (field == "$" || field.StartsWith("$.", StringComparison.Ordinal)
                    || (field.IndexOf('.') > 0 && !IsLegacyLlmField(field)))
                    return JsonPathResolver.ResolveText(Root, field);
                if (field.StartsWith("context.", StringComparison.OrdinalIgnoreCase))
                {
                    var key = field.Substring("context.".Length);
                    var hit = JsonPathResolver.ResolveText(Root, "$.context." + key);
                    if (hit is not null) return hit;
                }
            }

            if (string.IsNullOrEmpty(field) || field == "messages")
                return JoinMessages();
            if (field == "system") return System;
            if (field == "user") return User;
            if (field == "history") return RenderHistory();
            if (field!.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
            {
                var role = field.Substring(5);
                return string.Join("\n", RoleContents(role));
            }
            if (field.StartsWith("context.", StringComparison.OrdinalIgnoreCase))
            {
                var key = field.Substring("context.".Length);
                if (Context is not null && Context.TryGetValue(key, out var v))
                    return v?.ToString();
                return null;
            }
            return null;
        }

        /// <summary>field 是否为旧 LLM 形状保留键（role:&lt;名&gt; / context.&lt;key&gt; / system / user / history / messages），
        /// 用于判定是否应经 JSON 根按相对路径解析。</summary>
        private static bool IsLegacyLlmField(string field)
            => field.StartsWith("role:", StringComparison.OrdinalIgnoreCase)
               || field.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
               || field == "system" || field == "user" || field == "history" || field == "messages";

        /// <summary>某角色全部消息 content 列表（Role 匹配用）。</summary>
        public IReadOnlyList<string> RoleContents(string role)
        {
            if (Messages is not { Count: > 0 }) return Array.Empty<string>();
            return Messages.Where(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Content).ToArray();
        }

        /// <summary>完整对话文本（与旧 JoinMessages 语义一致）。</summary>
        public string JoinMessages()
        {
            if (Messages is { Count: > 0 })
                return RenderMessages(Messages);
            return (System ?? "") + "\n" + (User ?? "");
        }

        /// <summary>{history} 占位符。</summary>
        public string RenderHistory()
        {
            if (History is not { Count: > 0 }) return "";
            return RenderMessages(History);
        }

        /// <summary>{messages} 占位符。</summary>
        public string RenderMessages()
        {
            if (Messages is not { Count: > 0 }) return "";
            return RenderMessages(Messages);
        }

        private static string RenderMessages(IReadOnlyList<RuleMessage> list)
            => string.Join("\n", list.Select(m => m.Role + ": " + m.Content));

        /// <summary>最后一条 assistant 消息 content（{last_assistant}）。</summary>
        public string LastRoleContent(string role)
        {
            if (Messages is null) return "";
            for (var i = Messages.Count - 1; i >= 0; i--)
                if (string.Equals(Messages[i].Role, role, StringComparison.OrdinalIgnoreCase))
                    return Messages[i].Content;
            return "";
        }
    }

    /// <summary>引擎评估结果（LLM-free，由消费方适配为自身契约，如 LocalLlmClient 映射到 LlmResult）。</summary>
    public sealed class DecisionRuleResult
    {
        /// <summary>是否命中具名规则（false = 兜底 default）。</summary>
        public bool Matched { get; set; }

        /// <summary>命中的规则（兜底时为 Default）。</summary>
        public DecisionRule? MatchedRule { get; set; }

        /// <summary>是否走兜底。</summary>
        public bool IsFallback { get; set; }

        public OutputKind Kind { get; set; } = OutputKind.Text;

        /// <summary>text/json：渲染文本。</summary>
        public string? Text { get; set; }

        /// <summary>tool_call/tool_calls：工具调用列表。</summary>
        public IReadOnlyList<RuleToolCall>? ToolCalls { get; set; }

        /// <summary>structured：对象透传。</summary>
        public object? Structured { get; set; }

        /// <summary>delegate：委托名。</summary>
        public string? DelegateName { get; set; }
    }
}
