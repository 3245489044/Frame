using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core;
using Operation.LoopFoldMethod.Signal;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="RuleDecisionEvaluator"/> 决策信号化测试：默认规则集守卫内 Stop / 超步数守卫 Continue / stop_rule_quality ok=false Error。
    /// 零 LLM、纯 CI 可跑。
    /// </summary>
    [TestClass]
    public class RuleDecisionEvaluatorTests
    {
        [TestMethod]
        public void Evaluate_DefaultRuleSet_InGuard_Stops()
        {
            var eval = RuleDecisionEvaluator.Evaluate(new { ok = true, text = "完成" }, 1);
            Assert.AreEqual(FoldDecision.Stop, eval.Decision);
        }

        [TestMethod]
        public void Evaluate_DefaultRuleSet_BeyondMaxSteps_Continues()
        {
            // 默认规则集 max_steps=10；stepCount=11 超守卫 → continue（交由预算终止裁决）。
            var eval = RuleDecisionEvaluator.Evaluate(new { ok = true, text = "进行中" }, 11);
            Assert.AreEqual(FoldDecision.Continue, eval.Decision);
        }

        [TestMethod]
        public void Evaluate_StopRuleQuality_OkFalse_Errors()
        {
            // stop_rule_quality: answer_failed 条件 exec_result.ok==false → action=error。
            var eval = RuleDecisionEvaluator.Evaluate("{\"ok\":false,\"text\":\"失败\"}", 1, RuleSetLoader.Load("stop_rule_quality"));
            Assert.AreEqual(FoldDecision.Error, eval.Decision);
        }
    }
}
