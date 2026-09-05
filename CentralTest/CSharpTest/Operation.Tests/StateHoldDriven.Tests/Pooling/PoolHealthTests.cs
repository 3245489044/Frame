using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Share.StateManagement.Pooling;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// PoolHealthRegistry 单测——按状态聚合计数 / 状态变化触发 Changed / 健康判定。
    /// </summary>
    [TestClass]
    public class PoolHealthTests
    {
        [TestMethod]
        public void GetSnapshot_ShouldCountByState()
        {
            var engine = new StorageEngine();
            var store = new WorkerStateStore(engine);
            var health = new PoolHealthRegistry(store);

            store.GetOrCreate("w1"); store.StartWorker("w1");
            store.GetOrCreate("w2"); store.StartWorker("w2"); store.MarkBusy("w2");
            store.GetOrCreate("w3"); store.StartWorker("w3"); store.MarkHung("w3", "x");

            var snap = health.GetSnapshot();

            Assert.AreEqual(3, snap.Total);
            Assert.AreEqual(1, snap.Running);
            Assert.AreEqual(1, snap.Busy);
            Assert.AreEqual(1, snap.Hung);
            Assert.AreEqual(0, snap.Starting);
            Assert.AreEqual(0, snap.Stopped);
            Assert.IsFalse(snap.IsHealthy);
        }

        [TestMethod]
        public void Changed_ShouldFireOnStateChange()
        {
            var engine = new StorageEngine();
            var store = new WorkerStateStore(engine);
            var health = new PoolHealthRegistry(store);

            int fired = 0;
            health.Changed += _ => fired++;

            store.GetOrCreate("w1"); // Starting
            store.StartWorker("w1"); // Running

            Assert.AreEqual(2, fired);
        }

        [TestMethod]
        public void GetSnapshot_AllRunning_ShouldBeHealthy()
        {
            var engine = new StorageEngine();
            var store = new WorkerStateStore(engine);
            var health = new PoolHealthRegistry(store);

            store.GetOrCreate("w1"); store.StartWorker("w1");
            store.GetOrCreate("w2"); store.StartWorker("w2");

            var snap = health.GetSnapshot();
            Assert.IsTrue(snap.IsHealthy);
            Assert.AreEqual(2, snap.Running);
        }
    }
}
