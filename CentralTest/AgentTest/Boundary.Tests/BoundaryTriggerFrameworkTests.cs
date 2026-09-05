
using Agent.Contract.Boundaries;
using Agent.Axioms.Boundaries;
using Agent.Core.Boundaries;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 双通道触发框架单测：脊神经反应式 / 皮层主动式并行采集 + 仲裁合并。
    /// 覆盖主动式意图解析、主动式门控（白名单/置信度/advisory 降级）、仲裁优先级。
    /// </summary>
    [TestClass]
    [TestCategory("BoundaryFramework")]
    public class BoundaryTriggerFrameworkTests
    {
        private static BoundarySignal HealthySignal(int round = 1)
            => new(round, 5, 1000, "ok", EvalOutcome.Pass, null, 0, 0, 3);

        private static ContextEstimate HighContextEstimate(BoundaryConfig config)
            => new(config.Logical.ContextLimitChars, null);

        // ---- 脊神经反应式 ----

        [TestMethod]
        public void ReactiveTrigger_上下文压力超限_标记Reactive且ContextPressure()
        {
            var config = BoundaryConfig.Default;
            var a = new ReactiveBoundaryTrigger().Evaluate(
                HealthySignal(), config, HighContextEstimate(config), priorResultLength: 800);

            Assert.IsTrue(a.Triggered, "上下文压力超限应触发");
            Assert.AreEqual(TriggerChannel.Reactive, a.Channel);
            Assert.AreEqual(BoundaryTriggerKind.ContextPressure, a.TriggerKind);
            Assert.AreEqual(BoundaryActionKind.Compact, a.Action);
        }

        [TestMethod]
        public void ReactiveTrigger_协议硬断_短路种类为ProtocolStop()
        {
            var config = BoundaryConfig.Default;
            var signal = HealthySignal() with { StopReason = "model_context_window_exceeded" };

            var a = new ReactiveBoundaryTrigger().Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsTrue(a.Triggered, "协议硬断应触发");
            Assert.AreEqual(BoundaryTriggerKind.ProtocolStop, a.TriggerKind);
        }

        [TestMethod]
        public void ReactiveTrigger_信息增益速率在压力等价带内_归类为InfoGain()
        {
            // E5 回归：触发判定用 InfoGainPressure≥阈值，其等价速率阈值 = minRate+(1-threshold)(1-minRate)≈0.24。
            // rate=0.2（> minRate 0.05 但 ≤0.24）→ 旧逻辑（rate<=minRate）误归 Complexity，新逻辑正确归 InfoGain。
            var config = BoundaryConfig.Default;
            var signal = HealthySignal() with { SmoothedRate = 0.2 };

            var a = new ReactiveBoundaryTrigger().Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsTrue(a.Triggered, "InfoGainPressure≈0.84≥0.8 应触发");
            Assert.AreEqual(BoundaryTriggerKind.InfoGain, a.TriggerKind);
        }

        // ---- 主动式意图解析（皮层信号源） ----

        [TestMethod]
        public void ProactiveParser_识别重规划关键词_返回rescope()
        {
            var intent = ProactiveIntentParser.TryParse("上一步此路不通，需要语义重规划");

            Assert.IsNotNull(intent);
            Assert.AreEqual(BoundaryActionKind.Rescope, intent!.Action);
            Assert.AreEqual("text", intent.Source);
        }

        [TestMethod]
        public void ProactiveParser_无关键词_返回null()
        {
            Assert.IsNull(ProactiveIntentParser.TryParse("任务已完成，一切正常"));
        }

        [TestMethod]
        public void ProactiveParser_FromLlmAction_合法动作返回llm意图()
        {
            var intent = ProactiveIntentParser.FromLlmAction("compact");

            Assert.IsNotNull(intent);
            Assert.AreEqual(BoundaryActionKind.Compact, intent!.Action);
            Assert.AreEqual("llm", intent.Source);
        }

        [TestMethod]
        public void ProactiveParser_FromLlmAction_非法动作返回null()
        {
            Assert.IsNull(ProactiveIntentParser.FromLlmAction("do_random_stuff"));
        }

        // ---- 主动式门控 ----

        [TestMethod]
        public void ProactiveTrigger_健康信号_采纳主动式rescope()
        {
            var config = BoundaryConfig.Default;
            var signal = HealthySignal() with
            {
                Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Rescope, "text", 0.7)
            };

            var a = new ProactiveBoundaryTrigger().Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsTrue(a.Triggered, "白名单内高置信度主动式应触发");
            Assert.AreEqual(TriggerChannel.Proactive, a.Channel);
            Assert.AreEqual(BoundaryActionKind.Rescope, a.Action);
        }

        [TestMethod]
        public void ProactiveTrigger_白名单外动作_拒绝()
        {
            var config = BoundaryConfig.Default; // escalate 默认不在白名单
            var signal = HealthySignal() with
            {
                Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Escalate, "text", 0.9)
            };

            var a = new ProactiveBoundaryTrigger().Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsFalse(a.Triggered, "白名单外动作应被门控拒绝");
        }

        [TestMethod]
        public void ProactiveTrigger_低置信度_忽略()
        {
            var config = BoundaryConfig.Default;
            var signal = HealthySignal() with
            {
                Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Rescope, "text", 0.1)
            };

            var a = new ProactiveBoundaryTrigger().Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsFalse(a.Triggered, "低置信度自评应忽略");
        }

        [TestMethod]
        public void ProactiveTrigger_上下文压力低时compact_降级advisory()
        {
            var config = BoundaryConfig.Default;
            var signal = HealthySignal() with
            {
                Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Compact, "text", 0.9)
            };

            var a = new ProactiveBoundaryTrigger().Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsFalse(a.Triggered, "上下文压力低时主动式 compact 应降级为 advisory");
            Assert.IsNotNull(a.Reason);
            StringAssert.Contains(a.Reason!, "advisory");
        }

        // ---- 仲裁合并（并行 → 单一裁决） ----

        [TestMethod]
        public void Arbiter_反应式触发_短路优先于主动式()
        {
            var config = BoundaryConfig.Default;
            var reactive = new ReactiveBoundaryTrigger().Evaluate(
                HealthySignal(), config, HighContextEstimate(config), priorResultLength: 800);
            var proactive = new ProactiveBoundaryTrigger().Evaluate(
                HealthySignal() with { Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Rescope, "text", 0.7) },
                config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsTrue(reactive.Triggered && proactive.Triggered, "两通道均应触发");
            var merged = BoundaryTriggerArbiter.Merge(reactive, proactive, config.Triggers);

            Assert.IsTrue(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Reactive, merged.Channel, "脊神经应短路优先");
            Assert.AreEqual(reactive.Action, merged.Action);
        }

        [TestMethod]
        public void Arbiter_反应式健康且主动式触发_采纳主动式()
        {
            var config = BoundaryConfig.Default;
            var reactive = new ReactiveBoundaryTrigger().Evaluate(
                HealthySignal(), config, ContextEstimate.Empty, priorResultLength: 800);
            var proactive = new ProactiveBoundaryTrigger().Evaluate(
                HealthySignal() with { Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Rescope, "text", 0.7) },
                config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsFalse(reactive.Triggered, "反应式应健康");
            Assert.IsTrue(proactive.Triggered, "主动式应触发");
            var merged = BoundaryTriggerArbiter.Merge(reactive, proactive, config.Triggers);

            Assert.IsTrue(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Proactive, merged.Channel);
            Assert.AreEqual(BoundaryActionKind.Rescope, merged.Action);
        }

        [TestMethod]
        public void Arbiter_反应式关闭_仅采用主动式()
        {
            var config = BoundaryConfig.Default;
            config.Triggers.Reactive.Enabled = false;
            var reactive = BoundaryAssessment.Healthy;
            var proactive = new ProactiveBoundaryTrigger().Evaluate(
                HealthySignal() with { Proactive = new ProactiveBoundaryIntent(BoundaryActionKind.Rescope, "text", 0.7) },
                config, ContextEstimate.Empty, priorResultLength: 0);

            var merged = BoundaryTriggerArbiter.Merge(reactive, proactive, config.Triggers);

            Assert.IsTrue(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Proactive, merged.Channel);
        }

        [TestMethod]
        public void Arbiter_两者健康_保持健康()
        {
            var config = BoundaryConfig.Default;
            var reactive = new ReactiveBoundaryTrigger().Evaluate(
                HealthySignal(), config, ContextEstimate.Empty, priorResultLength: 800);
            var proactive = new ProactiveBoundaryTrigger().Evaluate(
                HealthySignal(), config, ContextEstimate.Empty, priorResultLength: 0);

            var merged = BoundaryTriggerArbiter.Merge(reactive, proactive, config.Triggers);

            Assert.IsFalse(merged.Triggered);
        }
    }
}
