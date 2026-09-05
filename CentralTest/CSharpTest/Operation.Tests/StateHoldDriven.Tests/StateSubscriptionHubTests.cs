using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.StateManagement;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// StateSubscriptionHub 单测——P2 中心核心验证：版本去重 / scope 过滤 / handler 异常隔离。
    /// </summary>
    [TestClass]
    public class StateSubscriptionHubTests
    {
        [TestMethod]
        public void Publish_WithOlderVersion_ShouldUpgradeNotDrop()
        {
            var hub = new StateSubscriptionHub();
            int count = 0;
            hub.Subscribe("ns", null, (_, _) => count++);

            hub.Publish(new StateKey("ns", "s", "k"), "v2", 2);
            hub.Publish(new StateKey("ns", "s", "k"), "v1", 1); // 陈旧版本 → 提升为 last+1（不丢弃，避免并发丢写）
            hub.Publish(new StateKey("ns", "s", "k"), "v3", 3); // v3 ≤ last(3) → 提升为 4

            Assert.AreEqual(3, count);
            Assert.AreEqual(4, hub.GetLastVersion("ns", "s"));
        }

        [TestMethod]
        public void Subscribe_WithScope_ShouldOnlyReceiveThatScope()
        {
            var hub = new StateSubscriptionHub();
            int count = 0;
            hub.Subscribe("ns", "A", (_, _) => count++);

            hub.Publish(new StateKey("ns", "A", "k1"), 1, 1);
            hub.Publish(new StateKey("ns", "B", "k2"), 2, 1);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void Subscribe_NullScope_ShouldReceiveAllScopes()
        {
            var hub = new StateSubscriptionHub();
            var keys = new List<string>();
            hub.Subscribe("ns", null, (key, _) => keys.Add(key.Scope));

            hub.Publish(new StateKey("ns", "A", "k1"), 1, 1);
            hub.Publish(new StateKey("ns", "B", "k2"), 2, 1);

            Assert.AreEqual(2, keys.Count);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, keys);
        }

        [TestMethod]
        public void Publish_HandlerException_ShouldNotAffectOthers()
        {
            var hub = new StateSubscriptionHub();
            int ok = 0;
            hub.Subscribe("ns", null, (_, _) => throw new InvalidOperationException("boom"));
            hub.Subscribe("ns", null, (_, _) => ok++);

            hub.Publish(new StateKey("ns", "s", "k"), "v", 1);

            Assert.AreEqual(1, ok);
        }

        [TestMethod]
        public void Snapshot_ShouldCarryValueAndVersion()
        {
            var hub = new StateSubscriptionHub();
            StateSnapshot<object?>? received = null;
            hub.Subscribe("ns", null, (_, snap) => received = snap);

            hub.Publish(new StateKey("ns", "s", "k"), 42, 7, "test-source");

            Assert.IsNotNull(received);
            Assert.AreEqual(42, received!.Value);
            Assert.AreEqual(7, received.Version);
            Assert.AreEqual("test-source", received.Source);
        }

        [TestMethod]
        public void Unsubscribe_ShouldStopDelivery()
        {
            var hub = new StateSubscriptionHub();
            int count = 0;
            var sub = hub.Subscribe("ns", null, (_, _) => count++);

            hub.Publish(new StateKey("ns", "s", "k"), 1, 1);
            sub.Dispose();
            hub.Publish(new StateKey("ns", "s", "k"), 2, 2);

            Assert.AreEqual(1, count);
        }
    }
}
