using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Share.StateManagement.Pooling;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// WorkerStateStore 单测——每 worker 会话隔离 + 状态机转移守卫 + StateChanged + 元数据落库。
    /// </summary>
    [TestClass]
    public class WorkerStateStoreTests
    {
        private static (StorageEngine Engine, WorkerStateStore Store) CreateStore()
        {
            var engine = new StorageEngine();
            return (engine, new WorkerStateStore(engine));
        }

        [TestMethod]
        public void GetOrCreate_ShouldRegisterStartingState_AndReturnCell()
        {
            var (_, store) = CreateStore();
            var cell = store.GetOrCreate("w1");

            Assert.IsNotNull(cell);
            Assert.IsTrue(store.Exists("w1"));
            var info = store.Get("w1");
            Assert.AreEqual(PoolWorkerState.Starting, info.State);
            Assert.AreEqual("w1", info.WorkerId);
            Assert.IsFalse(info.IsBusy);
        }

        [TestMethod]
        public void GetOrCreate_SameId_ShouldBeIdempotent()
        {
            var (_, store) = CreateStore();
            var c1 = store.GetOrCreate("w1");
            var c2 = store.GetOrCreate("w1");
            Assert.AreSame(c1, c2);
        }

        [TestMethod]
        public void Transition_WithWrongFrom_ShouldReturnFalse_AndKeepState()
        {
            var (_, store) = CreateStore();
            store.GetOrCreate("w1");

            // 当前 Starting，from=Running 非法 → 拒绝且状态不变
            Assert.IsFalse(store.Transition("w1", PoolWorkerState.Running, PoolWorkerState.Busy));
            Assert.AreEqual(PoolWorkerState.Starting, store.Get("w1").State);
        }

        [TestMethod]
        public void FullLifecycle_ShouldTransitionThroughStates()
        {
            var (_, store) = CreateStore();
            store.GetOrCreate("w1");

            Assert.IsTrue(store.StartWorker("w1"));
            Assert.AreEqual(PoolWorkerState.Running, store.Get("w1").State);

            Assert.IsTrue(store.MarkBusy("w1"));
            Assert.IsTrue(store.Get("w1").IsBusy);
            Assert.AreEqual(PoolWorkerState.Busy, store.Get("w1").State);

            Assert.IsTrue(store.MarkFree("w1"));
            Assert.AreEqual(PoolWorkerState.Running, store.Get("w1").State);

            Assert.IsTrue(store.MarkHung("w1", "watchdog timeout"));
            Assert.AreEqual(PoolWorkerState.Hung, store.Get("w1").State);
            Assert.AreEqual("watchdog timeout", store.Get("w1").LastError);

            Assert.IsTrue(store.StopWorker("w1"));
            Assert.AreEqual(PoolWorkerState.Stopped, store.Get("w1").State);
        }

        [TestMethod]
        public void StopWorker_ShouldReachTerminal_AndFurtherTransitionRejected()
        {
            var (_, store) = CreateStore();
            store.GetOrCreate("w1");
            store.StartWorker("w1");

            Assert.IsTrue(store.StopWorker("w1"));
            Assert.AreEqual(PoolWorkerState.Stopped, store.Get("w1").State);
            // 终态后 from 守卫拒绝再次转移
            Assert.IsFalse(store.Transition("w1", PoolWorkerState.Running, PoolWorkerState.Busy));
        }

        [TestMethod]
        public void Metadata_ShouldPersistPidAndTimeoutCount()
        {
            var (_, store) = CreateStore();
            store.GetOrCreate("w1");
            store.SetPid("w1", 4242);
            store.IncrementTimeoutCount("w1");
            store.IncrementTimeoutCount("w1");

            Assert.AreEqual(4242, store.Get("w1").Pid);
            Assert.AreEqual(2, store.Get("w1").TimeoutCount);
        }

        [TestMethod]
        public void Workers_ShouldBeIsolated()
        {
            var (_, store) = CreateStore();
            store.GetOrCreate("w1");
            store.StartWorker("w1");
            store.MarkBusy("w1");

            store.GetOrCreate("w2");

            Assert.AreEqual(PoolWorkerState.Busy, store.Get("w1").State);
            Assert.AreEqual(PoolWorkerState.Starting, store.Get("w2").State);
        }

        [TestMethod]
        public void StateChanged_ShouldFireOnRegistrationAndTransitions()
        {
            var (_, store) = CreateStore();
            var states = new List<PoolWorkerState>();
            store.StateChanged += i => states.Add(i.State);

            store.GetOrCreate("w1");
            store.StartWorker("w1");
            store.MarkBusy("w1");

            CollectionAssert.AreEqual(
                new[] { PoolWorkerState.Starting, PoolWorkerState.Running, PoolWorkerState.Busy },
                states);
        }

        [TestMethod]
        public void Remove_ShouldDisposeSession_AndDisappear()
        {
            var (engine, store) = CreateStore();
            store.GetOrCreate("w1");

            Assert.IsTrue(store.Remove("w1"));
            Assert.IsFalse(store.Exists("w1"));
            Assert.IsNull(engine.GetSession("w1"));
        }

        [TestMethod]
        public void Get_Unregistered_ShouldThrow()
        {
            var (_, store) = CreateStore();
            Assert.ThrowsException<KeyNotFoundException>(() => store.Get("nope"));
        }
    }
}
