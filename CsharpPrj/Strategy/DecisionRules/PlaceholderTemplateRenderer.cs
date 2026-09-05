using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Plate.JsonMethod.Utilities;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 占位符模板渲染器：{system}/{user}/{history}/{index}/{messages}/{last_assistant}/{last_message}/{role:&lt;名&gt;}。
    /// 复用 Plate.JsonMethod.TemplateFormatter 做统一替换（{role:&lt;名&gt;} 归一为字典键 "role:&lt;名&gt;"）。
    /// </summary>
    public sealed class PlaceholderTemplateRenderer : ITemplateRenderer
    {
        private readonly TemplateFormatter _formatter = new();

        public string Render(string? template, DecisionRuleContext ctx)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return _formatter.Format(template, BuildParameters(ctx));
        }

        private static IDictionary<string, object?> BuildParameters(DecisionRuleContext ctx)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["system"] = ctx.System,
                ["user"] = ctx.User,
                ["history"] = ctx.RenderHistory(),
                ["messages"] = ctx.RenderMessages(),
                ["last_assistant"] = ctx.LastRoleContent("assistant"),
                ["last_message"] = ctx.Messages is { Count: > 0 } ? ctx.Messages[^1].Content : "",
                ["index"] = ctx.Index?.ToString(CultureInfo.InvariantCulture) ?? "",
            };

            if (ctx.Messages is not null)
            {
                foreach (var role in ctx.Messages.Select(m => m.Role).Distinct(StringComparer.OrdinalIgnoreCase))
                    dict["role:" + role] = string.Join("\n", ctx.Messages
                        .Where(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase))
                        .Select(m => m.Content));
            }

            if (ctx.Context is not null)
            {
                foreach (var kv in ctx.Context)
                    dict["context." + kv.Key] = kv.Value;
            }

            return dict;
        }
    }
}
