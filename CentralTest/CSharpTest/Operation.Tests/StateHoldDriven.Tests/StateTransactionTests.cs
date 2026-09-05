using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Share.StateManagement;
using TConfig;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// StateTransaction 单测——P2 中心核心验证：Commit 落库+发布 / Rollback 不发布事件。
    /// </summary>
    [TestClass]
    public class StateTransactionTests
    {
        [TestMethod]
        public void Commit_ShouldWriteToCell_AndPublishOnce()
        {
            var cell = new StorageCell("tx");
            var hub = new StateSubscriptionHub();
            var received = new List<StateKey>();
            hub.Subscribe("container", "S", (key, _) => received.Add(key));

            using (var tx = new StateTransaction(cell, hub, "S"))
            {
                tx.Set(StateKey.Container("S"), "value1");
                tx.Commit();
            }

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("container:S:state", received[0].FullKey);
            Assert.AreEqual("value1", cell.Get<string>("container:S:state", StorageLayer.Session));
        }

        [TestMethod]
        public void Rollback_ShouldNotWriteCell_AndNotPublish()
        {
            var cell = new StorageCell("tx");
            var hub = new StateSubscriptionHub();
            int published = 0;
            hub.Subscribe("container", null, (_, _) => published++);

            using (var tx = new StateTransaction(cell, hub, "S"))
            {
                tx.Set(StateKey.Container("S"), "value1");
                tx.Rollback();
            }

            Assert.AreEqual(0, published);
            Assert.IsFalse(cell.Contains("container:S:state", StorageLayer.Session));
        }

        [TestMethod]
        public void Dispose_WithoutCommit_ShouldRollback()
        {
            var cell = new StorageCell("tx");
            var hub = new StateSubscriptionHub();
            int published = 0;
            hub.Subscribe("container", null, (_, _) => published++);

            using (var tx = new StateTransaction(cell, hub, "S"))
            {
                tx.Set(StateKey.Container("S"), "value1");
                // 不 Commit，Dispose 自动 Rollback
            }

            Assert.AreEqual(0, published);
            Assert.IsFalse(cell.Contains("container:S:state", StorageLayer.Session));
        }

        [TestMethod]
        public void Remove_ShouldDeleteCellAndPublish()
        {
            var cell = new StorageCell("tx");
            cell.Set("container:S:state", "old", StorageLayer.Session);
            var hub = new StateSubscriptionHub();
            int published = 0;
            hub.Subscribe("container", "S", (_, _) => published++);

            using (var tx = new StateTransaction(cell, hub, "S"))
            {
                tx.Remove(StateKey.Container("S"));
                tx.Commit();
            }

            Assert.AreEqual(1, published);
            Assert.IsFalse(cell.Contains("container:S:state", StorageLayer.Session));
        }

        [TestMethod]
        public void Commit_Twice_ShouldThrow()
        {
            var cell = new StorageCell("tx");
            var hub = new StateSubscriptionHub();
            var tx = new StateTransaction(cell, hub, "S");

            try
            {
                tx.Commit();
                Assert.ThrowsException<InvalidOperationException>(() => tx.Set(StateKey.Container("S"), "x"));
            }
            finally
            {
                tx.Dispose();
            }
        }
    }
}
