using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Tasks;
using Operation.HierarchyMethod.Tasks.Loops;
using Share.TaskStatusPool.TaskPool;

namespace HierarchyMethod.Tests.Tasks
{
    /// <summary>循环快照根（AgentLoopState）验证：确定性核心独立于 task 簿记，投影与还原。</summary>
    [TestClass]
    public class AgentLoopStateTests
    {
        [TestMethod]
        public void FromTaskRecord_ProjectsBookkeeping_DeterministicCoreFresh()
        {
            var record = new TaskRecord
            {
                TaskKey = "tk-1",
                UserInput = "hi",
                ThreadId = "th-1",
                InputKey = "in-1",
                Status = TaskExecutionStatus.NotStarted,
            };

            var state = AgentLoopState.FromTaskRecord(record);

            Assert.AreEqual("tk-1", state.TaskKey);
            Assert.AreEqual("hi", state.UserInput);
            Assert.AreEqual("th-1", state.ThreadId);
            Assert.AreEqual("in-1", state.InputKey);
            // 确定性核心从零开始（不继承任务簿记的循环状态）
            Assert.AreEqual(0, state.Token.Value);
            Assert.AreEqual(0, state.Budget.Level);
            Assert.AreEqual(0, state.Round);
            Assert.IsFalse(state.Flag.Halt);
        }

        [TestMethod]
        public void AgentLoopState_DeterministicCore_DefaultsToZero()
        {
            var state = new AgentLoopState();
            Assert.AreEqual(0, state.Token.Value);
            Assert.AreEqual(0, state.Budget.Level);
            Assert.AreEqual(0, state.Round);
            Assert.AreEqual(TaskExecutionStatus.NotStarted, state.Status);
        }
    }
}
