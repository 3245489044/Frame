using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Tasks;
using Share.HierarchySupport;

namespace HierarchyMethod.Tests.Tasks
{
    /// <summary>执行节点验证：LLM 细节经 WorkflowConfigKey 外置、Payload 不透明、纯数据可序列化。</summary>
    [TestClass]
    public class ExecutionNodeTests
    {
        [TestMethod]
        public void ExecutionNode_CarriesWorkflowConfigKey_ExternalizesLlmDetails()
        {
            var node = new ExecutionNode { ExecutorId = "stateless-answer", WorkflowConfigKey = "answer.v1", Payload = "{\"prompt\":\"...\"}" };
            Assert.AreEqual("stateless-answer", node.ExecutorId);
            Assert.AreEqual("answer.v1", node.WorkflowConfigKey); // 提示词外置，节点不内嵌
            Assert.IsNotNull(node.Payload); // 不透明透传
        }

        [TestMethod]
        public void ExecutionNode_DirectorMode_DefaultsNull_ZeroLlmTendency()
        {
            var node = new ExecutionNode();
            Assert.IsNull(node.DirectorMode); // null = 缺省（零 LLM 倾向）
        }

        [TestMethod]
        public void ExecutionNode_DirectorMode_ExplicitHybrid()
        {
            var node = new ExecutionNode { DirectorMode = SchedulingMode.Hybrid };
            Assert.AreEqual(SchedulingMode.Hybrid, node.DirectorMode);
        }
    }
}
