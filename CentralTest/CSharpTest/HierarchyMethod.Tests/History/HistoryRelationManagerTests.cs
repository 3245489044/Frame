using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.History;

namespace HierarchyMethod.Tests.History
{
    /// <summary>历史层验证：容器视图 / 链接记录 / 可空守卫（未装配守卫走诊断路径，LLM-free 自包含）。</summary>
    [TestClass]
    public class HistoryRelationManagerTests
    {
        [TestMethod]
        public void HistoryRecord_CarriesIdNameTasks()
        {
            var rec = new HistoryRecord("h-1", "会话 A", new[] { "t-1", "t-2" });
            Assert.AreEqual("h-1", rec.HistoryId);
            Assert.AreEqual("会话 A", rec.Name);
            Assert.AreEqual(2, rec.TaskKeys!.Count);
        }

        [TestMethod]
        public void HistoryTaskLink_CarriesHistoryAndTask()
        {
            var link = new HistoryTaskLink("h-1", "t-1");
            Assert.AreEqual("h-1", link.HistoryId);
            Assert.AreEqual("t-1", link.TaskKey);
        }

        [TestMethod]
        public void UnassembledManager_ReturnsDiagnostics_NotThrows()
        {
            var manager = new HistoryRelationManager(maintenance: null, graph: null);
            var rec = manager.CreateAsync("h-1").GetAwaiter().GetResult();
            Assert.AreEqual("h-1", rec.HistoryId);
            Assert.AreEqual(0, rec.TaskKeys!.Count);

            Assert.IsFalse(manager.DeleteAsync("h-1").GetAwaiter().GetResult());
            Assert.AreEqual(0, manager.GetTaskKeysAsync("h-1").GetAwaiter().GetResult().Count);
            Assert.AreEqual(0, manager.ListHistoryIdsAsync().GetAwaiter().GetResult().Count);
        }
    }
}
