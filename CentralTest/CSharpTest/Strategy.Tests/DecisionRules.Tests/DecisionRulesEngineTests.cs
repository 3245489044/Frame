using BasicMethod.DecisionEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Strategy.DecisionRules.Tests
{
    [TestClass]
    public class DecisionRulesEngineTests
    {
        private static DecisionRulesEngine NewEngine() => new();

        private static DecisionRuleSet RuleSet(params DecisionRule[] rules)
            => new() { Id = "test", Priority = RuleSetPriorityMode.Ordered, Rules = rules };

        [TestMethod]
        public void Evaluate_ContainsMatch_ReturnsTextAndRule()
        {
            var rs = RuleSet(new DecisionRule
            {
                Id = "r1",
                When = new WhenCondition
                {
                    All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "hello" } } },
                },
                Then = new OutputClause { Type = OutputKind.Text, Template = "hi" },
            });

            var res = NewEngine().Evaluate(rs, new DecisionRuleContext { User = "say hello" });

            Assert.IsTrue(res.Matched);
            Assert.IsFalse(res.IsFallback);
            Assert.AreEqual("r1", res.MatchedRule?.Id);
            Assert.AreEqual("hi", res.Text);
        }

        [TestMethod]
        public void Evaluate_NoMatch_UsesDefaultFallback()
        {
            var rs = RuleSet(new DecisionRule
            {
                Id = "r1",
                When = new WhenCondition
                {
                    All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "hello" } } },
                },
                Then = new OutputClause { Type = OutputKind.Text, Template = "hi" },
            });
            rs.Default = new DecisionRule { Id = "default", Then = new OutputClause { Type = OutputKind.Text, Template = "fallback" } };

            var res = NewEngine().Evaluate(rs, new DecisionRuleContext { User = "something else" });

            Assert.IsFalse(res.Matched);
            Assert.IsTrue(res.IsFallback);
            Assert.AreEqual("default", res.MatchedRule?.Id);
            Assert.AreEqual("fallback", res.Text);
        }

        [TestMethod]
        public void DeclaredPriority_SelectsHighestPriorityMatch()
        {
            var rs = new DecisionRuleSet
            {
                Id = "pri",
                Priority = RuleSetPriorityMode.Declared,
                Rules = new[]
                {
                    new DecisionRule { Id = "low", Priority = 1, Then = new OutputClause { Type = OutputKind.Text, Template = "low" } },
                    new DecisionRule { Id = "high", Priority = 10, Then = new OutputClause { Type = OutputKind.Text, Template = "high" } },
                },
            };

            // 两规则都恒命中（无 when），declared 模式应选 Priority=10。
            var res = NewEngine().Evaluate(rs, new DecisionRuleContext { User = "any" });

            Assert.AreEqual("high", res.MatchedRule?.Id);
            Assert.AreEqual("high", res.Text);
        }

        [TestMethod]
        public void ExpressionMatch_DelegatesToConditionEvaluator()
        {
            var rs = RuleSet(new DecisionRule
            {
                Id = "expr",
                When = new WhenCondition
                {
                    All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Expression, Value = "context.mode == A" } } },
                },
                Then = new OutputClause { Type = OutputKind.Text, Template = "matched-A" },
            });

            var engine = NewEngine();

            // context.mode == A → 命中（复用基础引擎 ConditionEvaluator）。
            var hit = engine.Evaluate(rs, new DecisionRuleContext { Context = new Dictionary<string, object?> { ["mode"] = "A" } });
            Assert.AreEqual("expr", hit.MatchedRule?.Id);

            // context.mode == B → 未命中 → 兜底空结果（Matched=false）。
            var miss = engine.Evaluate(rs, new DecisionRuleContext { Context = new Dictionary<string, object?> { ["mode"] = "B" } });
            Assert.IsFalse(miss.Matched);
            Assert.IsTrue(miss.IsFallback);
        }

        [TestMethod]
        public async Task RuleSetDecisionEngine_ComposesViaCompositeDecisionEngine()
        {
            // 底座适配：把两个声明式 RuleSet 包成 IDecisionEngine，再由 CompositeDecisionEngine 串联。
            var setA = RuleSet(new DecisionRule { Id = "a", Then = new OutputClause { Type = OutputKind.Text, Template = "A" } });
            var setB = RuleSet(new DecisionRule { Id = "b", Then = new OutputClause { Type = OutputKind.Text, Template = "B" } });

            var composite = new CompositeDecisionEngine<RuleDecisionOutcome, DecisionRuleContext>(
                    RuleDecisionOutcome.Empty, name: "composite")
                .AddEngine(new RuleSetDecisionEngine(setA, name: "setA"))
                .AddEngine(new RuleSetDecisionEngine(setB, name: "setB"));

            var result = await composite.DecideAsync(new DecisionRuleContext { User = "x" });

            Assert.IsNotNull(result);
            Assert.AreEqual("a", result.NextState.RuleId);
            Assert.AreEqual("A", result.NextState.Reasoning);
        }
    }
}
