using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Tasks;
using Share.TaskStatusPool.TaskPool;

namespace HierarchyMethod.Tests.Tasks
{
    /// <summary>task 层纯簿记验证（LLM-free，任务池独立可测）。</summary>
    [TestClass]
    public class TaskRecordTests
    {
        [TestMethod]
        public void TaskRecord_IsPureBookkeeping_DefaultStatus_NotStarted()
        {
            var record = new TaskRecord { TaskKey = "tk-1", UserInput = "hi", Priority = 2 };
            Assert.AreEqual("tk-1", record.TaskKey);
            Assert.AreEqual("hi", record.UserInput);
            Assert.AreEqual(TaskExecutionStatus.NotStarted, record.Status);
        }

        [TestMethod]
        public void TaskRecord_ExecutionNodeNull_IsDeterministicZeroLlm()
        {
            var record = new TaskRecord();
            Assert.IsNull(record.ExecutionNode); // 未指定执行策略 → 确定性路径（零 LLM）
        }

        [TestMethod]
        public void TaskRecord_CarriesExecutionNodeAsProperty()
        {
            var record = new TaskRecord { ExecutionNode = new ExecutionNode { ExecutorId = "stateless-answer" } };
            Assert.IsNotNull(record.ExecutionNode);
            Assert.AreEqual("stateless-answer", record.ExecutionNode!.ExecutorId);
        }
    }
}
