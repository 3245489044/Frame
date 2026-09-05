using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.StateManagement.Pooling;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// RequestTracker 单测——请求↔worker 关联 / 完成移除 / 按 worker 查询活跃请求。
    /// </summary>
    [TestClass]
    public class RequestTrackerTests
    {
        [TestMethod]
        public void Assign_ShouldTrackWorker_AndCount()
        {
            var tracker = new RequestTracker();
            var r1 = tracker.Assign("w1");
            var r2 = tracker.Assign("w1");
            var r3 = tracker.Assign("w2");

            Assert.AreEqual(3, tracker.ActiveRequestCount);
            Assert.AreEqual("w1", tracker.GetWorker(r1));
            Assert.AreEqual("w2", tracker.GetWorker(r3));
            CollectionAssert.AreEquivalent(
                new[] { r1, r2 },
                tracker.GetActiveRequestsForWorker("w1").ToArray());
        }

        [TestMethod]
        public void Complete_ShouldRemoveAssociation()
        {
            var tracker = new RequestTracker();
            var r1 = tracker.Assign("w1");

            Assert.IsTrue(tracker.Complete(r1));
            Assert.IsNull(tracker.GetWorker(r1));
            Assert.AreEqual(0, tracker.ActiveRequestCount);
            Assert.AreEqual(0, tracker.GetActiveRequestsForWorker("w1").Count);
            // 重复完成返回 false
            Assert.IsFalse(tracker.Complete(r1));
        }

        [TestMethod]
        public void GetWorker_UnknownRequest_ShouldReturnNull()
        {
            var tracker = new RequestTracker();
            Assert.IsNull(tracker.GetWorker("missing"));
        }

        [TestMethod]
        public void AfterDispose_ShouldThrowObjectDisposed()
        {
            var tracker = new RequestTracker();
            tracker.Assign("w1");

            tracker.Dispose();

            Assert.ThrowsException<ObjectDisposedException>(() => tracker.Assign("w2"));
            Assert.ThrowsException<ObjectDisposedException>(() => tracker.GetWorker("w1"));
            Assert.ThrowsException<ObjectDisposedException>(() => tracker.Complete("w1"));
            Assert.ThrowsException<ObjectDisposedException>(() => _ = tracker.ActiveRequestCount);
        }
    }
}
