using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.DecisionEngine;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 条件表达式匹配器（基础引擎桥接）：把声明式规则条件 "context.&lt;key&gt; == &lt;value&gt;" /
    /// "context.&lt;key&gt; == null" 委托给基础引擎 BasicMethod.DecisionEngine.ConditionEvaluator 求值。
    /// 复用其反射/字典取值缓存与 == / != 语义，不在框架层重复实现（SingleImplementation）。
    /// </summary>
    internal sealed class ConditionExpressionMatch : IMatchStrategy
    {
        private readonly ConditionEvaluator _evaluator = new();

        public MatchStrategy Kind => MatchStrategy.Expression;

        /// <summary>精确度对齐 JsonPath / Semantic（对 context 键值做等值判定）。</summary>
        public int Specificity => 6;

        public bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive)
        {
            var expr = clause.Value ?? clause.Field;
            if (string.IsNullOrWhiteSpace(expr)) return false;

            // 基础引擎 ConditionEvaluator 的 context.&lt;key&gt; 语法针对 IReadOnlyDictionary 取值；
            // 把 DecisionRuleContext.Context 展平为字典后委托求值（不直接反射 DecisionRuleContext 属性）。
            var dict = BuildContextDictionary(ctx);
            return _evaluator.Evaluate(expr, dict);
        }

        private static IReadOnlyDictionary<string, object?> BuildContextDictionary(DecisionRuleContext ctx)
        {
            if (ctx.Context is not { Count: > 0 }) return new Dictionary<string, object?>();
            // 保留既有 context 字典引用（ConditionEvaluator 支持 IReadOnlyDictionary<string,object?> 直读）。
            return ctx.Context;
        }
    }
}
