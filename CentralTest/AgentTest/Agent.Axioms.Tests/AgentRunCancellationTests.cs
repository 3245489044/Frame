using Agent.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests
{
    /// <summary>RunContext 任务键透传 + 暂停断 key（ClearTaskKey）冒烟测试。</summary>
    [TestClass]
    [TestCategory("Unit")]
    public sealed class AgentRunCancellationTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void TaskKey_SetClear_ReturnsNull()
        {
            using var rc = new RunContext(default, budget: null);
            using var enter = RunContext.Enter(rc);
            rc.SetTaskKey("task-1");
            Assert.AreEqual("task-1", RunContext.Current?.TaskKey);

            rc.ClearTaskKey(); // 暂停时断掉 key
            Assert.IsNull(RunContext.Current?.TaskKey);
        }
    }
}
