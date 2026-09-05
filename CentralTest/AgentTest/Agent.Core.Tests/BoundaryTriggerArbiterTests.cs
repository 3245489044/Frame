using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Axioms.Boundaries;       // BoundaryTriggerArbiter
using Agent.Contract.Boundaries;     // BoundaryAssessment / TriggerChannel / TriggerKind / BoundaryActionKind

namespace Agent.Core.Tests
{
    /// <summary>
    /// 边界触发仲裁器（小脑协调，Channel 轴 Reactive &gt; Proactive）合并语义回归基线（Phase 0）。
    /// </summary>
    [TestClass]
    [TestCategory("Boundary")]
    public class BoundaryTriggerArbiterTests
    {
        private static BoundaryAssessment ReactiveTriggered() =>
            BoundaryAssessment.Healthy with
            {
                Triggered = true,
                Action = BoundaryActionKind.Compact,
                Channel = TriggerChannel.Reactive,
            };

        private static BoundaryAssessment ProactiveTriggered() =>
            BoundaryAssessment.Healthy with
            {
                Triggered = true,
                Action = BoundaryActionKind.Rescope,
                Channel = TriggerChannel.Proactive,
                TriggerKind = BoundaryTriggerKind.LlmSelfAssessment,
            };

        [TestMethod]
        public void Merge_反应式触发_短路优先返回反应式()
        {
            var cfg = new BoundaryTriggerConfig(); // Reactive.Enabled = true 默认
            var merged = BoundaryTriggerArbiter.Merge(ReactiveTriggered(), ProactiveTriggered(), cfg);
            Assert.IsTrue(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Reactive, merged.Channel);
        }

        [TestMethod]
        public void Merge_反应式健康_采纳主动式()
        {
            var cfg = new BoundaryTriggerConfig();
            var merged = BoundaryTriggerArbiter.Merge(BoundaryAssessment.Healthy, ProactiveTriggered(), cfg);
            Assert.IsTrue(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Proactive, merged.Channel);
        }

        [TestMethod]
        public void Merge_反应式禁用_仅采纳主动式()
        {
            var cfg = new BoundaryTriggerConfig { Reactive = new ReactiveTriggerConfig { Enabled = false } };
            var merged = BoundaryTriggerArbiter.Merge(ReactiveTriggered(), ProactiveTriggered(), cfg);
            Assert.IsTrue(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Proactive, merged.Channel);
        }

        [TestMethod]
        public void Merge_双双健康_返回Healthy()
        {
            var cfg = new BoundaryTriggerConfig();
            var merged = BoundaryTriggerArbiter.Merge(BoundaryAssessment.Healthy, BoundaryAssessment.Healthy, cfg);
            Assert.IsFalse(merged.Triggered);
            Assert.AreEqual(TriggerChannel.Reactive, merged.Channel);
        }
    }
}
