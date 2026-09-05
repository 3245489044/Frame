using System;
using System.Collections.Generic;

namespace Strategy.DecisionRules
{
    /// <summary>决策规则路由结果（LLM-free，通用路由键 + 可选输出）。</summary>
    public sealed record DecisionRouteResult(
        string? RuleId,
        OutputKind Kind,
        string? Text,
        IReadOnlyList<RuleToolCall>? ToolCalls,
        bool IsFallback)
    {
        public bool Matched => RuleId is not null && !IsFallback;
    }

    /// <summary>
    /// 通用决策规则路由 facade：把「输入文本 → 裁决输出」封装为单一调用，供任意非 LLM 决策点复用
    /// （调度 / 路由 / 护栏 / 工具选择），验证引擎可脱离 LocalLlm 独立复用（SingleImplementation / TH-12）。
    /// </summary>
    public sealed class DecisionRuleRouter
    {
        private readonly DecisionRulesEngine _engine;
        private readonly DecisionRuleSet _ruleSet;

        public DecisionRuleRouter(DecisionRuleSet ruleSet, DecisionRulesEngine? engine = null)
        {
            _ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
            _engine = engine ?? new DecisionRulesEngine();
        }

        /// <summary>路由输入文本：命中返回匹配规则信息，未命中回退 default。context 供 context.&lt;key&gt; 字段取值。</summary>
        public DecisionRouteResult Route(string text, IReadOnlyDictionary<string, object?>? context = null)
        {
            var ctx = new DecisionRuleContext
            {
                User = text,
                Messages = new[] { new RuleMessage("user", text) },
                Context = context,
            };
            var res = _engine.Evaluate(_ruleSet, ctx);
            return new DecisionRouteResult(
                res.MatchedRule?.Id,
                res.Kind,
                res.Text,
                res.ToolCalls,
                res.IsFallback);
        }
    }
}
